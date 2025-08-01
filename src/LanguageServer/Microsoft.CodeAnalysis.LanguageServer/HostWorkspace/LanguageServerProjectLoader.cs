﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.LanguageServer.Handler.DebugConfiguration;
using Microsoft.CodeAnalysis.LanguageServer.HostWorkspace.ProjectTelemetry;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.ProjectSystem;
using Microsoft.CodeAnalysis.Shared.Collections;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.CodeAnalysis.Threading;
using Microsoft.CodeAnalysis.Workspaces.ProjectSystem;
using Microsoft.Extensions.Logging;
using Roslyn.Utilities;
using static Microsoft.CodeAnalysis.MSBuild.BuildHostProcessManager;
using LSP = Roslyn.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.LanguageServer.HostWorkspace;

internal abstract class LanguageServerProjectLoader
{
    private readonly AsyncBatchingWorkQueue<ProjectToLoad> _projectsToReload;

    private readonly ProjectTargetFrameworkManager _targetFrameworkManager;
    private readonly ProjectSystemHostInfo _projectSystemHostInfo;
    private readonly IFileChangeWatcher _fileChangeWatcher;
    protected readonly IGlobalOptionService GlobalOptionService;
    protected readonly ILoggerFactory LoggerFactory;
    private readonly ILogger _logger;
    private readonly ProjectLoadTelemetryReporter _projectLoadTelemetryReporter;
    private readonly IBinLogPathProvider _binLogPathProvider;
    protected readonly ImmutableDictionary<string, string> AdditionalProperties;

    /// <summary>
    /// Guards access to <see cref="_loadedProjects"/>.
    /// To keep the LSP queue responsive, <see cref="_gate"/> must not be held while performing design-time builds.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(initialCount: 1);

    /// <summary>
    /// Maps the file path of a tracked project to the load state for the project.
    /// Absence of an entry indicates the project is not tracked, e.g. it was never loaded, or it was unloaded.
    /// <see cref="_gate"/> must be held when modifying the dictionary or objects contained in it.
    /// </summary>
    private readonly Dictionary<string, ProjectLoadState> _loadedProjects = [];

    /// <summary>
    /// State transitions:
    /// <see cref="Primordial"/> -> <see cref="LoadedTargets"/>
    /// Any state -> unloaded (which is denoted by removing the <see cref="_loadedProjects"/> entry for the project)
    /// </summary>
    private abstract record ProjectLoadState
    {
        private ProjectLoadState() { }

        /// <summary>
        /// Represents a project which has not yet had a design-time build performed for it,
        /// and which has an associated "primordial project" in the workspace.
        /// </summary>
        /// <param name="PrimordialProjectFactory">
        /// The project factory for the workspace that the primordial project lives within. This
        /// factory was not used to create the project, but still needs to be used during removal to avoid locking issues.
        /// </param>
        /// <param name="PrimordialProjectId">
        /// ID of the project which LSP uses to fulfill requests until the first design-time build is complete.
        /// The project with this ID is removed from the workspace when unloading or when transitioning to <see cref="LoadedTargets"/> state.
        /// </param>
        public sealed record Primordial(ProjectSystemProjectFactory PrimordialProjectFactory, ProjectId PrimordialProjectId) : ProjectLoadState;

        /// <summary>
        /// Represents a project for which we have loaded zero or more targets.
        /// Generally a project which has zero loaded targets has not had a design-time build completed for it yet.
        /// Incrementally updated upon subsequent design-time builds.
        /// The <see cref="LoadedProjectTargets"/> are disposed when unloading.
        /// </summary>
        /// <param name="LoadedProjectTargets">List of target frameworks which have been loaded for this project so far.</param>
        public sealed record LoadedTargets(ImmutableArray<LoadedProject> LoadedProjectTargets) : ProjectLoadState;
    }

    protected LanguageServerProjectLoader(
        ProjectTargetFrameworkManager targetFrameworkManager,
        ProjectSystemHostInfo projectSystemHostInfo,
        IFileChangeWatcher fileChangeWatcher,
        IGlobalOptionService globalOptionService,
        ILoggerFactory loggerFactory,
        IAsynchronousOperationListenerProvider listenerProvider,
        ProjectLoadTelemetryReporter projectLoadTelemetry,
        ServerConfigurationFactory serverConfigurationFactory,
        IBinLogPathProvider binLogPathProvider)
    {
        _targetFrameworkManager = targetFrameworkManager;
        _projectSystemHostInfo = projectSystemHostInfo;
        _fileChangeWatcher = fileChangeWatcher;
        GlobalOptionService = globalOptionService;
        LoggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger(nameof(LanguageServerProjectLoader));
        _projectLoadTelemetryReporter = projectLoadTelemetry;
        _binLogPathProvider = binLogPathProvider;
        var razorDesignTimePath = serverConfigurationFactory.ServerConfiguration?.RazorDesignTimePath;

        AdditionalProperties = razorDesignTimePath is null
            ? ImmutableDictionary<string, string>.Empty
            : ImmutableDictionary<string, string>.Empty.Add("RazorDesignTimeTargets", razorDesignTimePath);

        _projectsToReload = new AsyncBatchingWorkQueue<ProjectToLoad>(
            TimeSpan.FromMilliseconds(100),
            ReloadProjectsAsync,
            ProjectToLoad.Comparer,
            listenerProvider.GetListener(FeatureAttribute.Workspace),
            CancellationToken.None); // TODO: do we need to introduce a shutdown cancellation token for this?
    }

    private sealed class ToastErrorReporter
    {
        private int _displayedToast = 0;

        public async Task ReportErrorAsync(LSP.MessageType errorKind, string message, CancellationToken cancellationToken)
        {
            // We should display a toast when the value of displayedToast is 0.  This will also update the value to 1 meaning we won't send any more toasts.
            var shouldShowToast = Interlocked.CompareExchange(ref _displayedToast, value: 1, comparand: 0) == 0;
            if (shouldShowToast)
            {
                await ShowToastNotification.ShowToastNotificationAsync(errorKind, message, cancellationToken, ShowToastNotification.ShowCSharpLogsCommand);
            }
        }
    }

    private async ValueTask ReloadProjectsAsync(ImmutableSegmentedList<ProjectToLoad> projectPathsToLoadOrReload, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // TODO: support configuration switching

        await using var buildHostProcessManager = new BuildHostProcessManager(globalMSBuildProperties: AdditionalProperties, binaryLogPathProvider: _binLogPathProvider, loggerFactory: LoggerFactory);
        var toastErrorReporter = new ToastErrorReporter();

        try
        {
            var projectsThatNeedRestore = await ProducerConsumer<string>.RunParallelAsync(
                source: projectPathsToLoadOrReload,
                produceItems: static async (projectToLoad, callback, args, cancellationToken) =>
                {
                    var (@this, toastErrorReporter, buildHostProcessManager) = args;
                    var projectNeedsRestore = await @this.ReloadProjectAsync(
                        projectToLoad, toastErrorReporter, buildHostProcessManager, cancellationToken);

                    if (projectNeedsRestore)
                        callback(projectToLoad.Path);
                },
                args: (@this: this, toastErrorReporter, buildHostProcessManager),
                cancellationToken).ConfigureAwait(false);

            if (GlobalOptionService.GetOption(LanguageServerProjectSystemOptionsStorage.EnableAutomaticRestore) && projectsThatNeedRestore.Any())
            {
                // Tell the client to restore any projects with unresolved dependencies.
                // This should eventually move entirely server side once we have a mechanism for reporting generic project load progress.
                // Tracking: https://github.com/dotnet/vscode-csharp/issues/6675
                //
                // The request blocks to ensure we aren't trying to run a design time build at the same time as a restore.
                await ProjectDependencyHelper.RestoreProjectsAsync(projectsThatNeedRestore, cancellationToken);
            }
        }
        finally
        {
            _logger.LogInformation(string.Format(LanguageServerResources.Completed_reload_of_all_projects_in_0, stopwatch.Elapsed));
        }
    }

    protected sealed record RemoteProjectLoadResult(RemoteProjectFile ProjectFile, ProjectSystemProjectFactory ProjectFactory, bool HasAllInformation, BuildHostProcessKind Preferred, BuildHostProcessKind Actual);

    /// <summary>Loads a project in the MSBuild host.</summary>
    /// <remarks>Caller needs to catch exceptions to avoid bringing down the project loader queue.</remarks>
    protected abstract Task<RemoteProjectLoadResult?> TryLoadProjectInMSBuildHostAsync(
        BuildHostProcessManager buildHostProcessManager, string projectPath, CancellationToken cancellationToken);

    /// <returns>True if the project needs a NuGet restore, false otherwise.</returns>
    private async Task<bool> ReloadProjectAsync(ProjectToLoad projectToLoad, ToastErrorReporter toastErrorReporter, BuildHostProcessManager buildHostProcessManager, CancellationToken cancellationToken)
    {
        BuildHostProcessKind? preferredBuildHostKindThatWeDidNotGet = null;
        var projectPath = projectToLoad.Path;
        Contract.ThrowIfFalse(PathUtilities.IsAbsolute(projectPath));

        // Before doing any work, check if the project has already been unloaded.
        using (await _gate.DisposableWaitAsync(cancellationToken))
        {
            if (!_loadedProjects.ContainsKey(projectPath))
            {
                return false;
            }
        }

        try
        {
            var remoteProjectLoadResult = await TryLoadProjectInMSBuildHostAsync(buildHostProcessManager, projectPath, cancellationToken);
            if (remoteProjectLoadResult is null)
            {
                // Note that this is a fairly common condition, e.g. for VB projects.
                // In the file-based programs primordial case, no 'LoadedProject' is produced for the project,
                // and therefore no reloading is performed for it after failing to load it once (in this code path).
                _logger.LogWarning($"Unable to load project '{projectPath}'.");
                return false;
            }

            (RemoteProjectFile remoteProjectFile, ProjectSystemProjectFactory projectFactory, bool hasAllInformation, BuildHostProcessKind preferredBuildHostKind, BuildHostProcessKind actualBuildHostKind) = remoteProjectLoadResult;
            if (preferredBuildHostKind != actualBuildHostKind)
                preferredBuildHostKindThatWeDidNotGet = preferredBuildHostKind;

            var diagnosticLogItems = await remoteProjectFile.GetDiagnosticLogItemsAsync(cancellationToken);
            if (diagnosticLogItems.Any(item => item.Kind is DiagnosticLogItemKind.Error))
            {
                await LogDiagnosticsAsync(diagnosticLogItems);
                // We have total failures in evaluation, no point in continuing.
                return false;
            }

            var loadedProjectInfos = await remoteProjectFile.GetProjectFileInfosAsync(cancellationToken);

            // The out-of-proc build host supports more languages than we may actually have Workspace binaries for, so ensure we can actually process that
            // language in-process.
            var projectLanguage = loadedProjectInfos.FirstOrDefault()?.Language;
            if (projectLanguage != null && projectFactory.Workspace.Services.GetLanguageService<ICommandLineParserService>(projectLanguage) == null)
            {
                return false;
            }

            Dictionary<ProjectFileInfo, ProjectLoadTelemetryReporter.TelemetryInfo> telemetryInfos = [];
            var needsRestore = false;

            using (await _gate.DisposableWaitAsync(cancellationToken))
            {
                if (!_loadedProjects.TryGetValue(projectPath, out var currentLoadState))
                {
                    // Project was unloaded. Do not proceed with reloading it.
                    return false;
                }

                var previousProjectTargets = currentLoadState is ProjectLoadState.LoadedTargets loaded ? loaded.LoadedProjectTargets : [];
                var newProjectTargetsBuilder = ArrayBuilder<LoadedProject>.GetInstance(loadedProjectInfos.Length);
                foreach (var loadedProjectInfo in loadedProjectInfos)
                {
                    var (target, targetAlreadyExists) = await GetOrCreateProjectTargetAsync(previousProjectTargets, projectFactory, loadedProjectInfo);
                    newProjectTargetsBuilder.Add(target);

                    var (targetTelemetryInfo, targetNeedsRestore) = await target.UpdateWithNewProjectInfoAsync(loadedProjectInfo, hasAllInformation, _logger);
                    needsRestore |= targetNeedsRestore;
                    if (!targetAlreadyExists)
                    {
                        telemetryInfos[loadedProjectInfo] = targetTelemetryInfo with { IsSdkStyle = preferredBuildHostKind == BuildHostProcessKind.NetCore };
                    }
                }

                var newProjectTargets = newProjectTargetsBuilder.ToImmutableAndFree();
                foreach (var target in previousProjectTargets)
                {
                    // Unload targets which were present in a past design-time build, but absent in the current one.
                    if (!newProjectTargets.Contains(target))
                    {
                        target.Dispose();
                    }
                }

                if (projectToLoad.ReportTelemetry)
                {
                    await _projectLoadTelemetryReporter.ReportProjectLoadTelemetryAsync(telemetryInfos, projectToLoad, cancellationToken);
                }

                if (currentLoadState is ProjectLoadState.Primordial(var primordialProjectFactory, var projectId))
                {
                    // Remove the primordial project now that the design-time build pass is finished. This ensures that
                    // we have the new project in place before we remove the primordial project; otherwise for
                    // Miscellaneous Files we could have a case where we'd get another request to create a project
                    // for the project we're currently processing.
                    await primordialProjectFactory.ApplyChangeToWorkspaceAsync(workspace => workspace.OnProjectRemoved(projectId), cancellationToken);
                }

                // At this point we expect that all the loaded projects are now in the project factory returned, and any previous ones have been removed.
                // this is a Debug.Assert() because if this expectation fails, the user's probably still in a state where things will work just fine;
                // throwing here would mean we don't remember the LoadedProjects we created, and the next update will create more and things will get really broken.
                Debug.Assert(newProjectTargets.All(target => target.ProjectFactory == projectFactory));
                _loadedProjects[projectPath] = new ProjectLoadState.LoadedTargets(newProjectTargets);
            }

            diagnosticLogItems = await remoteProjectFile.GetDiagnosticLogItemsAsync(cancellationToken);
            if (diagnosticLogItems.Any())
            {
                await LogDiagnosticsAsync(diagnosticLogItems);
            }
            else
            {
                _logger.LogInformation(string.Format(LanguageServerResources.Successfully_completed_load_of_0, projectPath));
            }

            return needsRestore;
        }
        catch (Exception e)
        {
            // Since our LogDiagnosticsAsync helper takes DiagnosticLogItems, let's just make one for this
            var message = string.Format(LanguageServerResources.Exception_thrown_0, e);
            var diagnosticLogItem = new DiagnosticLogItem(DiagnosticLogItemKind.Error, message, projectPath);
            await LogDiagnosticsAsync([diagnosticLogItem]);

            return false;
        }

        async Task<(LoadedProject, bool alreadyExists)> GetOrCreateProjectTargetAsync(ImmutableArray<LoadedProject> previousProjectTargets, ProjectSystemProjectFactory projectFactory, ProjectFileInfo loadedProjectInfo)
        {
            var existingProject = previousProjectTargets.FirstOrDefault(p => p.GetTargetFramework() == loadedProjectInfo.TargetFramework && p.ProjectFactory == projectFactory);
            if (existingProject != null)
            {
                return (existingProject, alreadyExists: true);
            }

            var targetFramework = loadedProjectInfo.TargetFramework;
            var projectSystemName = targetFramework is null ? projectPath : $"{projectPath} (${targetFramework})";

            var projectCreationInfo = new ProjectSystemProjectCreationInfo
            {
                AssemblyName = projectSystemName,
                FilePath = projectPath,
                CompilationOutputAssemblyFilePath = loadedProjectInfo.IntermediateOutputFilePath,
            };

            var projectSystemProject = await projectFactory.CreateAndAddToWorkspaceAsync(
                projectSystemName,
                loadedProjectInfo.Language,
                projectCreationInfo,
                _projectSystemHostInfo);

            var loadedProject = new LoadedProject(projectSystemProject, projectFactory, _fileChangeWatcher, _targetFrameworkManager);
            loadedProject.NeedsReload += (_, _) => _projectsToReload.AddWork(projectToLoad with { ReportTelemetry = false });
            return (loadedProject, alreadyExists: false);
        }

        async Task LogDiagnosticsAsync(ImmutableArray<DiagnosticLogItem> diagnosticLogItems)
        {
            foreach (var logItem in diagnosticLogItems)
            {
                var projectName = Path.GetFileName(projectPath);
                _logger.Log(logItem.Kind is DiagnosticLogItemKind.Error ? LogLevel.Error : LogLevel.Warning, $"{logItem.Kind} while loading {logItem.ProjectFilePath}: {logItem.Message}");
            }

            var worstLspMessageKind = diagnosticLogItems.Any(logItem => logItem.Kind is DiagnosticLogItemKind.Error) ? LSP.MessageType.Error : LSP.MessageType.Warning;

            string message;

            if (preferredBuildHostKindThatWeDidNotGet == BuildHostProcessKind.NetFramework)
                message = LanguageServerResources.Projects_failed_to_load_because_MSBuild_could_not_be_found;
            else if (preferredBuildHostKindThatWeDidNotGet == BuildHostProcessKind.Mono)
                message = LanguageServerResources.Projects_failed_to_load_because_Mono_could_not_be_found;
            else
                message = string.Format(LanguageServerResources.There_were_problems_loading_project_0_See_log_for_details, Path.GetFileName(projectPath));

            await toastErrorReporter.ReportErrorAsync(worstLspMessageKind, message, cancellationToken);
        }
    }

    protected async ValueTask<bool> IsProjectLoadedAsync(string projectPath, CancellationToken cancellationToken)
    {
        using (await _gate.DisposableWaitAsync(cancellationToken))
        {
            return _loadedProjects.ContainsKey(projectPath);
        }
    }

    /// <summary>
    /// Begins loading a project with an associated primordial project. Must not be called for a project which has already begun loading.
    /// </summary>
    /// <param name="doDesignTimeBuild">
    /// If <see langword="true"/>, initiates a design-time build now, and starts file watchers to repeat the design-time build on relevant changes.
    /// If <see langword="false"/>, only tracks the primordial project.
    /// </param>
    protected async ValueTask BeginLoadingProjectWithPrimordialAsync(string projectPath, ProjectSystemProjectFactory primordialProjectFactory, ProjectId primordialProjectId, bool doDesignTimeBuild)
    {
        using (await _gate.DisposableWaitAsync(CancellationToken.None))
        {
            // If this project has already begun loading, we need to throw.
            // This is because we can't ensure that the workspace and project system will remain in a consistent state after this call.
            // For example, there could be a need for the project system to track both a primordial project and list of loaded targets, which we don't support.
            if (_loadedProjects.ContainsKey(projectPath))
            {
                Contract.Fail($"Cannot begin loading project '{projectPath}' because it has already begun loading.");
            }

            _loadedProjects.Add(projectPath, new ProjectLoadState.Primordial(primordialProjectFactory, primordialProjectId));
            if (doDesignTimeBuild)
            {
                _projectsToReload.AddWork(new ProjectToLoad(projectPath, ProjectGuid: null, ReportTelemetry: true));
            }
        }
    }

    /// <summary>
    /// Begins loading a project. If the project has already begun loading, returns without doing any additional work.
    /// </summary>
    protected async Task BeginLoadingProjectAsync(string projectPath, string? projectGuid)
    {
        using (await _gate.DisposableWaitAsync(CancellationToken.None))
        {
            // If project has already begun loading, no need to do any further work.
            if (_loadedProjects.ContainsKey(projectPath))
            {
                return;
            }

            _loadedProjects.Add(projectPath, new ProjectLoadState.LoadedTargets(LoadedProjectTargets: []));
            _projectsToReload.AddWork(new ProjectToLoad(Path: projectPath, ProjectGuid: projectGuid, ReportTelemetry: true));
        }
    }

    protected Task WaitForProjectsToFinishLoadingAsync() => _projectsToReload.WaitUntilCurrentBatchCompletesAsync();

    protected async ValueTask UnloadProjectAsync(string projectPath)
    {
        using (await _gate.DisposableWaitAsync(CancellationToken.None))
        {
            if (!_loadedProjects.Remove(projectPath, out var loadState))
            {
                // It is common to be called with a path to a project which is already not loaded.
                // In this case, we should do nothing.
                return;
            }

            if (loadState is ProjectLoadState.Primordial(var projectFactory, var projectId))
            {
                await projectFactory.ApplyChangeToWorkspaceAsync(workspace => workspace.OnProjectRemoved(projectId));
            }
            else if (loadState is ProjectLoadState.LoadedTargets(var existingProjects))
            {
                foreach (var existingProject in existingProjects)
                {
                    // Disposing a LoadedProject unloads it and removes it from the workspace.
                    existingProject.Dispose();
                }
            }
            else
            {
                throw ExceptionUtilities.UnexpectedValue(loadState);
            }
        }
    }
}
