﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.CodeAnalysis.Editor.Shared.Utilities
Imports Microsoft.CodeAnalysis.Editor.UnitTests.Utilities
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.VisualStudio.LanguageServices.UnitTests.Utilities.VsNavInfo
Imports Microsoft.VisualStudio.Text.Editor.Commanding.Commands
Imports Roslyn.Test.Utilities

Namespace Microsoft.VisualStudio.LanguageServices.UnitTests.ClassView

    <UseExportProvider>
    <Trait(Traits.Feature, Traits.Features.ClassView)>
    Public Class SyncClassViewTests

#Region "C# Tests"

        <WpfFact>
        Public Sub CSharp_TestClass1()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
                    $$
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestClass2()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C$$
                {
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestClass3()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class $$C
                {
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestClass4()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                $$class C
                {
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestClass5()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {  $$
                    void M()
                    {
                    }
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestClassInNestedNamespaces1()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace X.Y
            {
                class C
                {  $$
                    void M()
                    {
                    }
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("X.Y"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestClassInNestedNamespaces2()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace X
            {
                namespace Y
                {
                    class C
                    {  $$
                        void M()
                        {
                        }
                    }
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("X.Y"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestMethod1()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
                    void M()
                    {$$
                    }
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("M()"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestMethod2()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
                    void M()
                    $${
                    }
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("M()"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestMethod3()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
                    void $$M()
                    {
                    }
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("M()"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestMethod4()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
                    $$void M()
                    {
                    }
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("M()"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestMethod5()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
            $$        void M()
                    {
                    }
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("M()"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestField1()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
            $$        int i;
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("i"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestField2()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
                    int $$i;
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("i"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestProperty1()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
            $$        int P { get; }
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("P"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestProperty2()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
                    int $$P { get; }
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("P"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestEvent1()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
            $$        event System.EventHandler E;
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("E"))
        End Sub

        <WpfFact>
        Public Sub CSharp_TestEvent2()
            Dim workspace =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSharpTestAssembly">
        <Document>
            namespace N
            {
                class C
                {
                    event System.EventHandler $$E;
                }
            }
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("CSharpTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("E"))
        End Sub

#End Region

#Region "Visual Basic Tests"

        <WpfFact>
        Public Sub VisualBasic_TestClass1()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
                    $$
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestClass2()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C$$
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestClass3()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class $$C
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestClass4()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                $$Class C
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestClass5()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C  $$
                    Sub M()
                    End Sub
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestClassInNestedNamespaces1()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace X.Y
                Class C$$
                    Sub M()
                    End Sub
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("X.Y"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestClassInNestedNamespaces2()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace X
                Namespace Y
                    Class C$$
                        Sub M()
                        End Sub
                    End Class
                End Namespace
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("X.Y"),
                ClassNode("C"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestMethod1()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
                    Sub M()
                        $$
                    End Sub
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("M()"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestMethod2()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
                    Sub M()$$
                    End Sub
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("M()"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestMethod3()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
                    Sub $$M()
                    End Sub
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("M()"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestMethod4()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
                    $$Sub M()
                    End Sub
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("M()"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestMethod5()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
            $$        Sub M()
                    End Sub
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("M()"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestField1()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
            $$        Private i As Integer
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("i As Integer"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestField2()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
                    Private $$i As Integer
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("i As Integer"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestProperty1()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
            $$        ReadOnly Property P As Integer = 42
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("P As Integer"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestProperty2()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
                    ReadOnly Property $$P As Integer = 42
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("P As Integer"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestEvent1()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
            $$        Event E()
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("E()"))
        End Sub

        <WpfFact>
        Public Sub VisualBasic_TestEvent2()
            Dim workspace =
<Workspace>
    <Project Language="Visual Basic" CommonReferences="true" AssemblyName="VBTestAssembly">
        <Document>
            Namespace N
                Class C
                    Event $$E()
                End Class
            End Namespace
        </Document>
    </Project>
</Workspace>

            Test(workspace,
                PackageNode("VBTestAssembly"),
                NamespaceNode("N"),
                ClassNode("C"),
                MemberNode("E()"))
        End Sub

#End Region

        Private Shared Sub Test(
            workspaceDefinition As XElement,
            ParamArray presentationNodes As NodeVerifier()
        )

            Using workspace = EditorTestWorkspace.Create(workspaceDefinition, composition:=VisualStudioTestCompositions.LanguageServices)
                Dim hostDocument = workspace.DocumentWithCursor
                Assert.True(hostDocument IsNot Nothing, "Test defined without cursor position")

                Dim textView = hostDocument.GetTextView()
                Dim subjectBuffer = hostDocument.GetTextBuffer()

                Dim navigationTool = New MockNavigationTool(canonicalNodes:=Nothing, presentationNodes:=presentationNodes)
                Dim serviceProvider = New MockServiceProvider(navigationTool)
                Dim commandHandler = New MockSyncClassViewCommandHandler(workspace.ExportProvider.GetExportedValue(Of IThreadingContext), serviceProvider)

                commandHandler.ExecuteCommand(
                    args:=New SyncClassViewCommandArgs(textView, subjectBuffer), TestCommandExecutionContext.Create())

                navigationTool.VerifyNavInfo()
            End Using

        End Sub

    End Class
End Namespace
