﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    internal abstract class SourceUserDefinedOperatorSymbolBase : SourceOrdinaryMethodOrUserDefinedOperatorSymbol
    {
        // tomat: ignoreDynamic should be true, but we don't want to introduce breaking change. See bug 605326.
        private const TypeCompareKind ComparisonForUserDefinedOperators = TypeCompareKind.IgnoreTupleNames | TypeCompareKind.IgnoreNullableModifiersForReferenceTypes;
        private readonly string _name;
#nullable enable
        private readonly TypeSymbol? _explicitInterfaceType;
#nullable disable

        protected SourceUserDefinedOperatorSymbolBase(
            MethodKind methodKind,
            TypeSymbol explicitInterfaceType,
            string name,
            bool isCompoundAssignmentOrIncrementAssignment,
            SourceMemberContainerTypeSymbol containingType,
            Location location,
            CSharpSyntaxNode syntax,
            DeclarationModifiers declarationModifiers,
            bool hasAnyBody,
            bool isExpressionBodied,
            bool isIterator,
            bool isNullableAnalysisEnabled,
            BindingDiagnosticBag diagnostics) :
            base(containingType, syntax.GetReference(), location, isIterator: isIterator,
                 (declarationModifiers, MakeFlags(
                                                  methodKind, RefKind.None, declarationModifiers,
                                                  // We will bind the formal parameters and the return type lazily. For now,
                                                  // assume that the return type is non-void; when we do the lazy initialization
                                                  // of the parameters and return type we will update the flag if necessary.
                                                  returnsVoid: false,
                                                  returnsVoidIsSet: false,
                                                  isExpressionBodied: isExpressionBodied,
                                                  isExtensionMethod: false, isVarArg: false, isNullableAnalysisEnabled: isNullableAnalysisEnabled,
                                                  isExplicitInterfaceImplementation: methodKind == MethodKind.ExplicitInterfaceImplementation,
                                                  hasThisInitializer: false)))
        {
            _explicitInterfaceType = explicitInterfaceType;
            _name = name;

            this.CheckUnsafeModifier(declarationModifiers, diagnostics);

            if (isCompoundAssignmentOrIncrementAssignment)
            {
                Debug.Assert(!this.IsStatic);
                Binder.CheckFeatureAvailability(syntax, MessageID.IDS_FeatureUserDefinedCompoundAssignmentOperators, diagnostics, ((OperatorDeclarationSyntax)syntax).OperatorToken.GetLocation());
            }

            if (this.ContainingType.IsInterface &&
                !(IsAbstract || IsVirtual) && !IsExplicitInterfaceImplementation &&
                !(syntax is OperatorDeclarationSyntax { OperatorToken: var opToken } && opToken.Kind() is not (SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken)))
            {
                diagnostics.Add(ErrorCode.ERR_InterfacesCantContainConversionOrEqualityOperators, this.GetFirstLocation());
                // No need to cascade the error further.
                return;
            }

            if (this.ContainingType.IsStatic)
            {
                // Similarly if we're in a static class, though we have not reported it yet.

                // CS0715: '{0}': static classes cannot contain user-defined operators
                diagnostics.Add(ErrorCode.ERR_OperatorInStaticClass, location, this);
                return;
            }

            if (this.ContainingType is { IsExtension: true, ExtensionParameter.Type.IsStatic: true })
            {
                diagnostics.Add(ErrorCode.ERR_OperatorInExtensionOfStaticClass, location);
                return;
            }

            // SPEC: An operator declaration must include both a public and a
            // SPEC: static modifier
            if (this.IsExplicitInterfaceImplementation)
            {
                if (!this.IsStatic && !isCompoundAssignmentOrIncrementAssignment)
                {
                    diagnostics.Add(ErrorCode.ERR_ExplicitImplementationOfOperatorsMustBeStatic, this.GetFirstLocation(), this);
                }
            }
            else if (isCompoundAssignmentOrIncrementAssignment)
            {
                if (this.DeclaredAccessibility != Accessibility.Public)
                {
                    diagnostics.Add(ErrorCode.ERR_OperatorsMustBePublic, this.GetFirstLocation(), this);
                }
            }
            else if (this.DeclaredAccessibility != Accessibility.Public || !this.IsStatic)
            {
                // CS0558: User-defined operator '...' must be declared static and public
                diagnostics.Add(ErrorCode.ERR_OperatorsMustBeStaticAndPublic, this.GetFirstLocation(), this);
            }

            // SPEC: Because an external operator provides no actual implementation, 
            // SPEC: its operator body consists of a semicolon. For expression-bodied
            // SPEC: operators, the body is an expression. For all other operators,
            // SPEC: the operator body consists of a block...
            if (IsAbstract && IsExtern)
            {
                diagnostics.Add(ErrorCode.ERR_AbstractAndExtern, location, this);
            }
            else if (IsAbstract && IsVirtual)
            {
                diagnostics.Add(ErrorCode.ERR_AbstractNotVirtual, location, this.Kind.Localize(), this);
            }
            else if (hasAnyBody && (IsExtern || IsAbstract))
            {
                Debug.Assert(!(IsAbstract && IsExtern));
                if (IsExtern)
                {
                    diagnostics.Add(ErrorCode.ERR_ExternHasBody, location, this);
                }
                else
                {
                    diagnostics.Add(ErrorCode.ERR_AbstractHasBody, location, this);
                }
            }
            else if (!hasAnyBody && !IsExtern && !IsAbstract && !IsPartial)
            {
                // Do not report that the body is missing if the operator is marked as
                // partial or abstract; we will already have given an error for that so
                // there's no need to "cascade" the error.
                diagnostics.Add(ErrorCode.ERR_ConcreteMissingBody, location, this);
            }
            else if (IsOverride && (IsNew || IsVirtual))
            {
                // A member '{0}' marked as override cannot be marked as new or virtual
                diagnostics.Add(ErrorCode.ERR_OverrideNotNew, location, this);
            }
            else if (IsSealed && !IsOverride && !(IsExplicitInterfaceImplementation && ContainingType.IsInterface && IsAbstract))
            {
                // '{0}' cannot be sealed because it is not an override
                diagnostics.Add(ErrorCode.ERR_SealedNonOverride, location, this);
            }
            else if (IsAbstract && IsSealed && !IsExplicitInterfaceImplementation)
            {
                diagnostics.Add(ErrorCode.ERR_AbstractAndSealed, location, this);
            }
            else if (IsAbstract && !ContainingType.IsAbstract && !ContainingType.IsInterface)
            {
                // '{0}' is abstract but it is contained in non-abstract type '{1}'
                diagnostics.Add(ErrorCode.ERR_AbstractInConcreteClass, location, this, ContainingType);
            }
            else if (IsVirtual && ContainingType.IsSealed)
            {
                // '{0}' is a new virtual member in sealed type '{1}'
                diagnostics.Add(ErrorCode.ERR_NewVirtualInSealed, location, this, ContainingType);
            }

            // SPEC: It is an error for the same modifier to appear multiple times in an
            // SPEC: operator declaration.
            ModifierUtils.CheckAccessibility(this.DeclarationModifiers, this, isExplicitInterfaceImplementation: false, diagnostics, location);

            if (isCompoundAssignmentOrIncrementAssignment)
            {
                _ = Binder.GetWellKnownTypeMember(DeclaringCompilation, WellKnownMember.System_Runtime_CompilerServices_CompilerFeatureRequiredAttribute__ctor, diagnostics, location);
            }
        }

        protected static DeclarationModifiers MakeDeclarationModifiers(bool isCompoundAssignmentOrIncrementAssignment, MethodKind methodKind, SourceMemberContainerTypeSymbol containingType, BaseMethodDeclarationSyntax syntax, Location location, BindingDiagnosticBag diagnostics)
        {
            bool inInterface = containingType.IsInterface;
            bool inExtension = containingType.IsExtension;
            bool isExplicitInterfaceImplementation = methodKind == MethodKind.ExplicitInterfaceImplementation;
            var defaultAccess = inInterface && !isExplicitInterfaceImplementation ? DeclarationModifiers.Public : DeclarationModifiers.Private;
            var allowedModifiers =
                DeclarationModifiers.Unsafe | DeclarationModifiers.Extern;

            if (!isCompoundAssignmentOrIncrementAssignment)
            {
                allowedModifiers |= DeclarationModifiers.Static;
            }

            if (!isExplicitInterfaceImplementation)
            {
                allowedModifiers |= DeclarationModifiers.AccessibilityMask;

                if (inInterface)
                {
                    allowedModifiers |= DeclarationModifiers.Abstract | DeclarationModifiers.Virtual;

                    if (syntax is OperatorDeclarationSyntax { OperatorToken: var opToken } && opToken.Kind() is not (SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken))
                    {
                        allowedModifiers |= DeclarationModifiers.Sealed;
                    }
                }

                if (isCompoundAssignmentOrIncrementAssignment && !inExtension)
                {
                    if (inInterface)
                    {
                        allowedModifiers |= DeclarationModifiers.New;
                    }
                    else
                    {
                        if (containingType.IsClassType())
                        {
                            allowedModifiers |= DeclarationModifiers.Abstract | DeclarationModifiers.Virtual | DeclarationModifiers.Sealed;
                        }

                        allowedModifiers |= DeclarationModifiers.Override | DeclarationModifiers.New;
                    }
                }
            }
            else if (inInterface)
            {
                Debug.Assert(isExplicitInterfaceImplementation);
                allowedModifiers |= DeclarationModifiers.Abstract;
            }

            if (containingType.IsStructType() && isCompoundAssignmentOrIncrementAssignment)
            {
                allowedModifiers |= DeclarationModifiers.ReadOnly;
            }

            var result = ModifierUtils.MakeAndCheckNonTypeMemberModifiers(
                isOrdinaryMethod: false, isForInterfaceMember: inInterface,
                syntax.Modifiers, defaultAccess, allowedModifiers, location, diagnostics, modifierErrors: out _, hasExplicitAccessModifier: out _);

            if (inInterface)
            {
                if ((result & (DeclarationModifiers.Abstract | DeclarationModifiers.Virtual | DeclarationModifiers.Sealed)) != 0)
                {
                    if ((result & DeclarationModifiers.Sealed) != 0 &&
                        (result & (DeclarationModifiers.Abstract | DeclarationModifiers.Virtual)) != 0)
                    {
                        diagnostics.Add(ErrorCode.ERR_BadMemberFlag, location, ModifierUtils.ConvertSingleModifierToSyntaxText(DeclarationModifiers.Sealed));
                        result &= ~DeclarationModifiers.Sealed;
                    }

                    LanguageVersion availableVersion = ((CSharpParseOptions)location.SourceTree.Options).LanguageVersion;
                    LanguageVersion requiredVersion = MessageID.IDS_FeatureStaticAbstractMembersInInterfaces.RequiredVersion();

                    if (availableVersion < requiredVersion)
                    {
                        var requiredVersionArgument = new CSharpRequiredLanguageVersion(requiredVersion);
                        var availableVersionArgument = availableVersion.ToDisplayString();

                        if ((result & DeclarationModifiers.Abstract) != 0)
                        {
                            reportModifierIfPresent(result, DeclarationModifiers.Abstract, location, diagnostics, requiredVersionArgument, availableVersionArgument);
                        }
                        else
                        {
                            reportModifierIfPresent(result, DeclarationModifiers.Virtual, location, diagnostics, requiredVersionArgument, availableVersionArgument);
                        }

                        reportModifierIfPresent(result, DeclarationModifiers.Sealed, location, diagnostics, requiredVersionArgument, availableVersionArgument);
                    }

                    result &= ~DeclarationModifiers.Sealed;
                }
                else if ((result & DeclarationModifiers.Static) != 0)
                {
                    if (syntax is OperatorDeclarationSyntax { OperatorToken: var opToken } && opToken.Kind() is not (SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken))
                    {
                        Binder.CheckFeatureAvailability(location.SourceTree, MessageID.IDS_DefaultInterfaceImplementation, diagnostics, location);
                    }
                }
                else if (!isExplicitInterfaceImplementation && isCompoundAssignmentOrIncrementAssignment)
                {
                    if (syntax.HasAnyBody())
                    {
                        result |= DeclarationModifiers.Virtual;
                    }
                    else
                    {
                        result |= DeclarationModifiers.Abstract;
                    }
                }
            }

            if (isExplicitInterfaceImplementation)
            {
                if ((result & DeclarationModifiers.Abstract) != 0)
                {
                    result |= DeclarationModifiers.Sealed;
                }
            }

            return result;

            static void reportModifierIfPresent(DeclarationModifiers result, DeclarationModifiers errorModifier, Location location, BindingDiagnosticBag diagnostics, CSharpRequiredLanguageVersion requiredVersionArgument, string availableVersionArgument)
            {
                if ((result & errorModifier) != 0)
                {
                    diagnostics.Add(ErrorCode.ERR_InvalidModifierForLanguageVersion, location,
                                    ModifierUtils.ConvertSingleModifierToSyntaxText(errorModifier),
                                    availableVersionArgument,
                                    requiredVersionArgument);
                }
            }
        }

        protected (TypeWithAnnotations ReturnType, ImmutableArray<ParameterSymbol> Parameters) MakeParametersAndBindReturnType(BaseMethodDeclarationSyntax declarationSyntax, TypeSyntax returnTypeSyntax, BindingDiagnosticBag diagnostics)
        {
            TypeWithAnnotations returnType;
            ImmutableArray<ParameterSymbol> parameters;

            var binder = this.DeclaringCompilation.
                GetBinderFactory(declarationSyntax.SyntaxTree).GetBinder(returnTypeSyntax, declarationSyntax, this);

            SyntaxToken arglistToken;

            var signatureBinder = binder.WithAdditionalFlags(BinderFlags.SuppressConstraintChecks);

            parameters = ParameterHelpers.MakeParameters(
                signatureBinder,
                this,
                declarationSyntax.ParameterList,
                out arglistToken,
                allowRefOrOut: true,
                allowThis: false,
                addRefReadOnlyModifier: IsVirtual || IsAbstract,
                diagnostics: diagnostics).Cast<SourceParameterSymbol, ParameterSymbol>();

            if (arglistToken.Kind() == SyntaxKind.ArgListKeyword)
            {
                // This is a parse-time error in the native compiler; it is a semantic analysis error in Roslyn.

                // error CS1669: __arglist is not valid in this context
                diagnostics.Add(ErrorCode.ERR_IllegalVarArgs, new SourceLocation(arglistToken));

                // Regardless of whether __arglist appears in the source code, we do not mark
                // the operator method as being a varargs method.
            }

            returnType = signatureBinder.BindType(returnTypeSyntax, diagnostics);

            // restricted types cannot be returned. 
            // NOTE: Span-like types can be returned (if expression is returnable).
            if (returnType.IsRestrictedType(ignoreSpanLikeTypes: true))
            {
                // The return type of a method, delegate, or function pointer cannot be '{0}'
                diagnostics.Add(ErrorCode.ERR_MethodReturnCantBeRefAny, returnTypeSyntax.Location, returnType.Type);
            }

            if (returnType.Type.IsStatic)
            {
                // Operators in interfaces was introduced in C# 8, so there's no need to be specially concerned about
                // maintaining backcompat with the native compiler bug around interfaces.
                // '{0}': static types cannot be used as return types
                diagnostics.Add(ErrorFacts.GetStaticClassReturnCode(useWarning: false), returnTypeSyntax.Location, returnType.Type);
            }

            return (returnType, parameters);
        }

        protected override void MethodChecks(BindingDiagnosticBag diagnostics)
        {
            var (returnType, parameters) = MakeParametersAndBindReturnType(diagnostics);

            MethodChecks(returnType, parameters, diagnostics);

            // If we have a static class then we already 
            // have reported that fact as an error. No need to cascade the error further.
            if (this.ContainingType.IsStatic)
            {
                return;
            }

            CheckValueParameters(diagnostics);
            CheckOperatorSignatures(diagnostics);
        }

        protected abstract (TypeWithAnnotations ReturnType, ImmutableArray<ParameterSymbol> Parameters) MakeParametersAndBindReturnType(BindingDiagnosticBag diagnostics);

        protected sealed override void ExtensionMethodChecks(BindingDiagnosticBag diagnostics)
        {
            if (ContainingType is { IsExtension: true, ExtensionParameter: { Type.IsStatic: false } extensionParameter } && !IsStatic &&
                OperatorFacts.IsCompoundAssignmentOperatorName(Name))
            {
                if (extensionParameter.Name == "")
                {
                    diagnostics.Add(ErrorCode.ERR_InstanceMemberWithUnnamedExtensionsParameter, _location, new FormattedSymbol(this, SymbolDisplayFormat.ShortFormat));
                }

                // Require receiver type to be known as a class or as a struct, and
                // require:
                //     - struct receiver to be a 'ref' 
                //     - class receiver to be 'by val' (not 'ref', not 'in', not 'ref readonly')

                // We don't report every invalid combination here in order to avoid producing too much noise.
                switch (extensionParameter.RefKind)
                {
                    case RefKind.Out: // 'out' is disallowed in general
                    case RefKind.Ref: // 'ref' receivers are disallowed for types not known to be a struct
                        break;

                    case RefKind.In:
                    case RefKind.RefReadOnlyParameter:
                        // 'in' and 'ref readonly' receivers are disallowed for anything that is not a concrete struct (class or a type parameter)
                        if (extensionParameter.Type.IsStructType())
                        {
                            diagnostics.Add(ErrorCode.ERR_InstanceOperatorStructExtensionWrongReceiverRefKind, _location);
                        }

                        break;

                    case RefKind.None:
                        switch (extensionParameter.Type)
                        {
                            case { IsValueType: true }:
                                diagnostics.Add(ErrorCode.ERR_InstanceOperatorStructExtensionWrongReceiverRefKind, _location);
                                break;

                            case { TypeKind: TypeKind.TypeParameter, IsReferenceType: false }:
                                diagnostics.Add(ErrorCode.ERR_InstanceOperatorExtensionWrongReceiverType, _location);
                                break;
                        }

                        break;

                    default:
                        throw ExceptionUtilities.UnexpectedValue(extensionParameter.RefKind);
                }
            }
        }

        protected sealed override MethodSymbol FindExplicitlyImplementedMethod(BindingDiagnosticBag diagnostics)
        {
            if (_explicitInterfaceType is object)
            {
                string interfaceMethodName;
                ExplicitInterfaceSpecifierSyntax explicitInterfaceSpecifier;

                switch (syntaxReferenceOpt.GetSyntax())
                {
                    case OperatorDeclarationSyntax operatorDeclaration:
                        interfaceMethodName = OperatorFacts.OperatorNameFromDeclaration(operatorDeclaration);
                        explicitInterfaceSpecifier = operatorDeclaration.ExplicitInterfaceSpecifier;
                        break;

                    case ConversionOperatorDeclarationSyntax conversionDeclaration:
                        interfaceMethodName = OperatorFacts.OperatorNameFromDeclaration(conversionDeclaration);
                        explicitInterfaceSpecifier = conversionDeclaration.ExplicitInterfaceSpecifier;
                        break;

                    default:
                        throw ExceptionUtilities.Unreachable();
                }

                return this.FindExplicitlyImplementedMethod(isOperator: true, _explicitInterfaceType, interfaceMethodName, explicitInterfaceSpecifier, diagnostics);
            }

            return null;
        }

#nullable enable
        protected sealed override TypeSymbol? ExplicitInterfaceType => _explicitInterfaceType;
#nullable disable

        private void CheckValueParameters(BindingDiagnosticBag diagnostics)
        {
            // SPEC: The parameters of an operator must be value parameters.
            foreach (var p in this.Parameters)
            {
                if (p.RefKind != RefKind.None && p.RefKind != RefKind.In)
                {
                    diagnostics.Add(ErrorCode.ERR_IllegalRefParam, this.GetFirstLocation());
                    break;
                }
            }
        }

        private void CheckOperatorSignatures(BindingDiagnosticBag diagnostics)
        {
            if (MethodKind == MethodKind.ExplicitInterfaceImplementation)
            {
                // The signature is driven by the interface
                return;
            }

            // Have we even got the right formal parameter arity? If not then 
            // we are in an error recovery scenario and we should just bail 
            // out immediately.
            if (!DoesOperatorHaveCorrectArity(this.Name, this.ParameterCount))
            {
                return;
            }

            switch (this.Name)
            {
                case WellKnownMemberNames.ImplicitConversionName:
                case WellKnownMemberNames.ExplicitConversionName:
                case WellKnownMemberNames.CheckedExplicitConversionName:
                    CheckUserDefinedConversionSignature(diagnostics);
                    break;

                case WellKnownMemberNames.CheckedUnaryNegationOperatorName:
                case WellKnownMemberNames.UnaryNegationOperatorName:
                case WellKnownMemberNames.UnaryPlusOperatorName:
                case WellKnownMemberNames.LogicalNotOperatorName:
                case WellKnownMemberNames.OnesComplementOperatorName:
                    CheckUnarySignature(diagnostics);
                    break;

                case WellKnownMemberNames.TrueOperatorName:
                case WellKnownMemberNames.FalseOperatorName:
                    CheckTrueFalseSignature(diagnostics);
                    break;

                case WellKnownMemberNames.CheckedIncrementOperatorName:
                case WellKnownMemberNames.IncrementOperatorName:
                case WellKnownMemberNames.CheckedDecrementOperatorName:
                case WellKnownMemberNames.DecrementOperatorName:
                    CheckIncrementDecrementSignature(diagnostics);
                    break;

                case WellKnownMemberNames.LeftShiftOperatorName:
                case WellKnownMemberNames.RightShiftOperatorName:
                case WellKnownMemberNames.UnsignedRightShiftOperatorName:
                    CheckShiftSignature(diagnostics);
                    break;

                case WellKnownMemberNames.EqualityOperatorName:
                case WellKnownMemberNames.InequalityOperatorName:
                    if (IsInInterfaceAndAbstractOrVirtual())
                    {
                        CheckAbstractEqualitySignature(diagnostics);
                    }
                    else
                    {
                        CheckBinarySignature(diagnostics);
                    }

                    break;

                case WellKnownMemberNames.CheckedAdditionAssignmentOperatorName:
                case WellKnownMemberNames.AdditionAssignmentOperatorName:
                case WellKnownMemberNames.CheckedDivisionAssignmentOperatorName:
                case WellKnownMemberNames.DivisionAssignmentOperatorName:
                case WellKnownMemberNames.CheckedMultiplicationAssignmentOperatorName:
                case WellKnownMemberNames.MultiplicationAssignmentOperatorName:
                case WellKnownMemberNames.CheckedSubtractionAssignmentOperatorName:
                case WellKnownMemberNames.SubtractionAssignmentOperatorName:
                case WellKnownMemberNames.ModulusAssignmentOperatorName:
                case WellKnownMemberNames.BitwiseAndAssignmentOperatorName:
                case WellKnownMemberNames.BitwiseOrAssignmentOperatorName:
                case WellKnownMemberNames.ExclusiveOrAssignmentOperatorName:
                case WellKnownMemberNames.LeftShiftAssignmentOperatorName:
                case WellKnownMemberNames.RightShiftAssignmentOperatorName:
                case WellKnownMemberNames.UnsignedRightShiftAssignmentOperatorName:
                case WellKnownMemberNames.CheckedIncrementAssignmentOperatorName:
                case WellKnownMemberNames.IncrementAssignmentOperatorName:
                case WellKnownMemberNames.CheckedDecrementAssignmentOperatorName:
                case WellKnownMemberNames.DecrementAssignmentOperatorName:
                    if (!this.ReturnsVoid)
                    {
                        diagnostics.Add(ErrorCode.ERR_OperatorMustReturnVoid, this.GetFirstLocation());
                    }

                    break;

                default:
                    CheckBinarySignature(diagnostics);
                    break;
            }
        }
        private bool IsInInterfaceAndAbstractOrVirtual()
        {
            return ContainingType.IsInterface && (IsAbstract || IsVirtual);
        }

        private static bool DoesOperatorHaveCorrectArity(string name, int parameterCount)
        {
            switch (name)
            {
                case WellKnownMemberNames.CheckedIncrementOperatorName:
                case WellKnownMemberNames.IncrementOperatorName:
                case WellKnownMemberNames.CheckedDecrementOperatorName:
                case WellKnownMemberNames.DecrementOperatorName:
                    return parameterCount == 1;

                case WellKnownMemberNames.CheckedUnaryNegationOperatorName:
                case WellKnownMemberNames.UnaryNegationOperatorName:
                case WellKnownMemberNames.UnaryPlusOperatorName:
                case WellKnownMemberNames.LogicalNotOperatorName:
                case WellKnownMemberNames.OnesComplementOperatorName:
                case WellKnownMemberNames.TrueOperatorName:
                case WellKnownMemberNames.FalseOperatorName:
                case WellKnownMemberNames.ImplicitConversionName:
                case WellKnownMemberNames.ExplicitConversionName:
                case WellKnownMemberNames.CheckedExplicitConversionName:
                case WellKnownMemberNames.CheckedAdditionAssignmentOperatorName:
                case WellKnownMemberNames.AdditionAssignmentOperatorName:
                case WellKnownMemberNames.CheckedDivisionAssignmentOperatorName:
                case WellKnownMemberNames.DivisionAssignmentOperatorName:
                case WellKnownMemberNames.CheckedMultiplicationAssignmentOperatorName:
                case WellKnownMemberNames.MultiplicationAssignmentOperatorName:
                case WellKnownMemberNames.CheckedSubtractionAssignmentOperatorName:
                case WellKnownMemberNames.SubtractionAssignmentOperatorName:
                case WellKnownMemberNames.ModulusAssignmentOperatorName:
                case WellKnownMemberNames.BitwiseAndAssignmentOperatorName:
                case WellKnownMemberNames.BitwiseOrAssignmentOperatorName:
                case WellKnownMemberNames.ExclusiveOrAssignmentOperatorName:
                case WellKnownMemberNames.LeftShiftAssignmentOperatorName:
                case WellKnownMemberNames.RightShiftAssignmentOperatorName:
                case WellKnownMemberNames.UnsignedRightShiftAssignmentOperatorName:
                    return parameterCount == 1;

                case WellKnownMemberNames.CheckedIncrementAssignmentOperatorName:
                case WellKnownMemberNames.IncrementAssignmentOperatorName:
                case WellKnownMemberNames.CheckedDecrementAssignmentOperatorName:
                case WellKnownMemberNames.DecrementAssignmentOperatorName:
                    return parameterCount == 0;
                default:
                    return parameterCount == 2;
            }
        }

        private void CheckUserDefinedConversionSignature(BindingDiagnosticBag diagnostics)
        {
            CheckReturnIsNotVoid(diagnostics);

            // SPEC: For a given source type S and target type T, if S or T are
            // SPEC: nullable types let S0 and T0 refer to their underlying types,
            // SPEC: otherwise, S0 and T0 are equal to S and T, respectively.

            var source = this.GetParameterType(0);
            var target = this.ReturnType;
            var source0 = source.StrippedType();
            var target0 = target.StrippedType();

            // SPEC: A class or struct is permitted to declare a conversion from S to T
            // SPEC: only if all the following are true:

            // SPEC: Neither S0 nor T0 is an interface type.

            if (source0.IsInterfaceType() || target0.IsInterfaceType())
            {
                // CS0552: '{0}': user-defined conversions to or from an interface are not allowed
                diagnostics.Add(ErrorCode.ERR_ConversionWithInterface, this.GetFirstLocation(), this);
                return;
            }

            // SPEC: Either S0 or T0 is the class or struct type in which the operator
            // SPEC: declaration takes place.

            if (!MatchesContainingType(source0, checkStrippedType: false) &&
                !MatchesContainingType(target0, checkStrippedType: false) &&
                // allow conversion between T and Nullable<T> in declaration of Nullable<T>
                !MatchesContainingType(source, checkStrippedType: false) &&
                !MatchesContainingType(target, checkStrippedType: false))
            {
                // CS0556: User-defined conversion must convert to or from the enclosing type
                diagnostics.Add(IsInInterfaceAndAbstractOrVirtual() ? ErrorCode.ERR_AbstractConversionNotInvolvingContainedType : ErrorCode.ERR_ConversionNotInvolvingContainedType, this.GetFirstLocation());
                return;
            }

            // SPEC: * S0 and T0 are different types:

            if ((ContainingType.SpecialType == SpecialType.System_Nullable_T)
                    ? source.Equals(target, ComparisonForUserDefinedOperators)
                    : source0.Equals(target0, ComparisonForUserDefinedOperators))
            {
                // CS0555: User-defined operator cannot convert a type to itself
                diagnostics.Add(ErrorCode.ERR_IdentityConversion, this.GetFirstLocation());
                return;
            }

            // Those are the easy ones. Now we come to:

            // SPEC: 
            // Excluding user-defined conversions, a conversion does not exist from 
            // S to T or T to S. For the purposes of these rules, any type parameters
            // associated with S or T are considered to be unique types that have
            // no inheritance relationship with other types, and any constraints on
            // those type parameters are ignored.

            // A counter-intuitive consequence of this rule is that:
            //
            // class X<U> where U : X<U>
            // {
            //     public implicit operator X<U>(U u) { return u; }
            // }
            //
            // is *legal*, even though there is *already* an implicit conversion
            // from U to X<U> because U is constrained to have such a conversion.
            //
            // In discussing the implications of this rule, let's call the 
            // containing type (which may be a class or struct) "C". S and T
            // are the source and target types.  
            //
            // If we have made it this far in the error analysis we already know that
            // exactly one of S and T is C or C? -- if two or zero were, then we'd
            // have already reported ERR_ConversionNotInvolvingContainedType or 
            // ERR_IdentityConversion and returned.
            //
            // WOLOG for the purposes of this discussion let's assume that S is 
            // the one that is C or C?, and that T is the one that is neither C nor C?.
            //
            // So the question is: under what circumstances could T-to-S or S-to-T,
            // be a valid conversion, by the definition of valid above?
            //
            // Let's consider what kinds of types T could be. T cannot be an interface
            // because we've already reported an error and returned if it is. If T is
            // a delegate, array, enum, pointer, struct or nullable type then there 
            // is no built-in conversion from T to the user-declared class/struct 
            // C, or to C?. If T is a type parameter, then by assumption the type
            // parameter has no constraints, and therefore is not convertible to
            // C or C?. 
            //
            // That leaves T to be a class. We already know that T is not C, (or C?, 
            // since T is a class) and therefore there is no identity conversion from T to S.
            //
            // Suppose S is C and C is a class. Then the only way that there can be a 
            // conversion between T and S is if T is a base class of S or S is a base class of T.
            //
            // Suppose S is C and C is a struct. Then the only way that there can be a
            // conversion between T and S is if T is a base class of S. (And T would
            // have to be System.Object or System.ValueType.)
            //
            // Suppose S is C? and C is a struct. Then the only way that there can be a 
            // conversion between T and S is again, if T is a base class of S.
            //
            // Summing up:
            //
            // WOLOG, we assume that T is not C or C?, and S is C or C?. The conversion is
            // illegal only if T is a class, and either T is a base class of S, or S is a 
            // base class of T.

            if (source.IsDynamic() || target.IsDynamic())
            {
                // '{0}': user-defined conversions to or from the dynamic type are not allowed
                diagnostics.Add(ErrorCode.ERR_BadDynamicConversion, this.GetFirstLocation(), this);
                return;
            }

            TypeSymbol same;
            TypeSymbol different;

            if (MatchesContainingType(source0, checkStrippedType: false))
            {
                same = source;
                different = target;
            }
            else
            {
                same = target;
                different = source;
            }

            if (different.IsClassType() && !same.IsTypeParameter())
            {
                // different is a class type:
                Debug.Assert(!different.IsTypeParameter());

                var useSiteInfo = new CompoundUseSiteInfo<AssemblySymbol>(diagnostics, ContainingAssembly);

                if (same.IsDerivedFrom(different, ComparisonForUserDefinedOperators, useSiteInfo: ref useSiteInfo))
                {
                    // '{0}': user-defined conversions to or from a base type are not allowed
                    diagnostics.Add(ErrorCode.ERR_ConversionWithBase, this.GetFirstLocation(), this);
                }
                else if (different.IsDerivedFrom(same, ComparisonForUserDefinedOperators, useSiteInfo: ref useSiteInfo))
                {
                    // '{0}': user-defined conversions to or from a derived type are not allowed
                    diagnostics.Add(ErrorCode.ERR_ConversionWithDerived, this.GetFirstLocation(), this);
                }

                diagnostics.Add(this.GetFirstLocation(), useSiteInfo);
            }
        }

        private void CheckReturnIsNotVoid(BindingDiagnosticBag diagnostics)
        {
            if (this.ReturnsVoid)
            {
                // CS0590: User-defined operators cannot return void
                diagnostics.Add(ErrorCode.ERR_OperatorCantReturnVoid, this.GetFirstLocation());
            }
        }

        private void CheckUnarySignature(BindingDiagnosticBag diagnostics)
        {
            // SPEC: A unary + - ! ~ operator must take a single parameter of type
            // SPEC: T or T? and can return any type.

            CheckUnaryParameterType(diagnostics);
            CheckReturnIsNotVoid(diagnostics);
        }

        private void CheckUnaryParameterType(BindingDiagnosticBag diagnostics)
        {
            if (!MatchesContainingType(this.GetParameterType(0), checkStrippedType: true))
            {
                // The parameter of a unary operator must be the containing type
                diagnostics.Add(IsInInterfaceAndAbstractOrVirtual() ?
                                    ErrorCode.ERR_BadAbstractUnaryOperatorSignature :
                                    (ContainingType.IsExtension ?
                                         ErrorCode.ERR_BadExtensionUnaryOperatorSignature :
                                         ErrorCode.ERR_BadUnaryOperatorSignature),
                                this.GetFirstLocation());
            }
        }

        private void CheckTrueFalseSignature(BindingDiagnosticBag diagnostics)
        {
            // SPEC: A unary true or false operator must take a single parameter of type
            // SPEC: T or T? and must return type bool.

            if (this.ReturnType.SpecialType != SpecialType.System_Boolean)
            {
                // The return type of operator True or False must be bool
                diagnostics.Add(ErrorCode.ERR_OpTFRetType, this.GetFirstLocation());
            }

            CheckUnaryParameterType(diagnostics);
        }

        private void CheckIncrementDecrementSignature(BindingDiagnosticBag diagnostics)
        {
            // SPEC: A unary ++ or -- operator must take a single parameter of type T or T?
            // SPEC: and it must return that same type or a type derived from it.

            // The native compiler error reporting behavior is not very good in some cases
            // here, both because it reports the wrong errors, and because the wording
            // of the error messages is misleading. The native compiler reports two errors:

            // CS0448: The return type for ++ or -- operator must be the 
            //         containing type or derived from the containing type
            //
            // CS0559: The parameter type for ++ or -- operator must be the containing type
            //
            // Neither error message mentions nullable types. But worse, there is a 
            // situation in which the native compiler reports a misleading error:
            //
            // struct S { public static S operator ++(S? s) { ... } }
            //
            // This reports CS0559, but that is not the error; the *parameter* is perfectly
            // legal. The error is that the return type does not match the parameter type.
            // 
            // I have changed the error message to reflect the true error, and we now 
            // report 0448, not 0559, in the given scenario. The error is now:
            //
            // CS0448: The return type for ++ or -- operator must match the parameter type
            //         or be derived from the parameter type
            //
            // However, this now means that we must make *another* change from native compiler
            // behavior. The native compiler would report both 0448 and 0559 when given:
            //
            // struct S { public static int operator ++(int s) { ... } }
            //
            // The previous wording of error 0448 was *correct* in this scenario, but not
            // it is wrong because it *does* match the formal parameter type.
            //
            // The solution is: First see if 0559 must be reported. Only if the formal
            // parameter type is *good* do we then go on to try to report an error against
            // the return type.

            var parameterType = this.GetParameterType(0);
            var useSiteInfo = new CompoundUseSiteInfo<AssemblySymbol>(diagnostics, ContainingAssembly);

            if (!MatchesContainingType(parameterType, checkStrippedType: true))
            {
                // CS0559: The parameter type for ++ or -- operator must be the containing type
                diagnostics.Add(IsInInterfaceAndAbstractOrVirtual() ?
                                    ErrorCode.ERR_BadAbstractIncDecSignature :
                                    (ContainingType.IsExtension ?
                                         ErrorCode.ERR_BadExtensionIncDecSignature :
                                         ErrorCode.ERR_BadIncDecSignature),
                                this.GetFirstLocation());
            }
            else if (!(parameterType.IsTypeParameter() ?
                         this.ReturnType.Equals(parameterType, ComparisonForUserDefinedOperators) :
                         ((IsInInterfaceAndAbstractOrVirtual() && IsContainingType(parameterType) && IsSelfConstrainedTypeParameter(this.ReturnType)) ||
                             this.ReturnType.EffectiveTypeNoUseSiteDiagnostics.IsEqualToOrDerivedFrom(parameterType, ComparisonForUserDefinedOperators, useSiteInfo: ref useSiteInfo))))
            {
                // CS0448: The return type for ++ or -- operator must match the parameter type
                //         or be derived from the parameter type
                diagnostics.Add(IsInInterfaceAndAbstractOrVirtual() ? ErrorCode.ERR_BadAbstractIncDecRetType : ErrorCode.ERR_BadIncDecRetType, this.GetFirstLocation());
            }

            diagnostics.Add(this.GetFirstLocation(), useSiteInfo);
        }

        private bool MatchesContainingType(TypeSymbol type, bool checkStrippedType)
        {
            if (ContainingType is { IsExtension: true, ExtensionParameter.Type: var extendedType })
            {
                if (extendedType is null)
                {
                    return true; // An error scenario
                }

                return ExtensionOperatorParameterTypeMatchesExtendedType(type, extendedType);
            }

            if (checkStrippedType)
            {
                type = type.StrippedType();
            }

            return IsContainingType(type) ||
                   (IsInInterfaceAndAbstractOrVirtual() && IsSelfConstrainedTypeParameter(type));
        }

        internal static bool ExtensionOperatorParameterTypeMatchesExtendedType(TypeSymbol type, TypeSymbol extendedType)
        {
            return type.Equals(extendedType, ComparisonForUserDefinedOperators);
        }

        private bool IsContainingType(TypeSymbol type)
        {
            return type.Equals(this.ContainingType, ComparisonForUserDefinedOperators);
        }

        public static bool IsSelfConstrainedTypeParameter(TypeSymbol type, NamedTypeSymbol containingType)
        {
            Debug.Assert(containingType.IsDefinition);
            return type is TypeParameterSymbol p &&
                (object)p.ContainingSymbol == containingType &&
                p.ConstraintTypesNoUseSiteDiagnostics.Any((typeArgument, containingType) => typeArgument.Type.Equals(containingType, ComparisonForUserDefinedOperators),
                                                          containingType);
        }

        private bool IsSelfConstrainedTypeParameter(TypeSymbol type)
        {
            return IsSelfConstrainedTypeParameter(type, this.ContainingType);
        }

        private void CheckShiftSignature(BindingDiagnosticBag diagnostics)
        {
            // SPEC: A binary <<, >> or >>> operator must take two parameters, the first
            // SPEC: of which must have type T or T?, the second of which can
            // SPEC: have any type. The operator can return any type.

            if (!MatchesContainingType(this.GetParameterType(0), checkStrippedType: true))
            {
                // CS0546: The first operand of an overloaded shift operator must have the 
                //         same type as the containing type
                diagnostics.Add(IsInInterfaceAndAbstractOrVirtual() ?
                                    ErrorCode.ERR_BadAbstractShiftOperatorSignature :
                                    (ContainingType.IsExtension ? ErrorCode.ERR_BadExtensionShiftOperatorSignature : ErrorCode.ERR_BadShiftOperatorSignature),
                                this.GetFirstLocation());
            }
            else if (this.GetParameterType(1).StrippedType().SpecialType != SpecialType.System_Int32)
            {
                var location = this.GetFirstLocation();
                Binder.CheckFeatureAvailability(location.SourceTree, MessageID.IDS_FeatureRelaxedShiftOperator, diagnostics, location);
            }

            CheckReturnIsNotVoid(diagnostics);
        }

        private void CheckBinarySignature(BindingDiagnosticBag diagnostics)
        {
            // SPEC: A binary nonshift operator must take two parameters, at least
            // SPEC: one of which must have the type T or T?, and can return any type.
            if (!MatchesContainingType(this.GetParameterType(0), checkStrippedType: true) &&
                !MatchesContainingType(this.GetParameterType(1), checkStrippedType: true))
            {
                // CS0563: One of the parameters of a binary operator must be the containing type
                diagnostics.Add(IsInInterfaceAndAbstractOrVirtual() ?
                                    ErrorCode.ERR_BadAbstractBinaryOperatorSignature :
                                    (ContainingType.IsExtension ? ErrorCode.ERR_BadExtensionBinaryOperatorSignature : ErrorCode.ERR_BadBinaryOperatorSignature),
                                this.GetFirstLocation());
            }

            CheckReturnIsNotVoid(diagnostics);
        }

        private void CheckAbstractEqualitySignature(BindingDiagnosticBag diagnostics)
        {
            if (!IsSelfConstrainedTypeParameter(this.GetParameterType(0).StrippedType()) &&
                !IsSelfConstrainedTypeParameter(this.GetParameterType(1).StrippedType()))
            {
                diagnostics.Add(ErrorCode.ERR_BadAbstractEqualityOperatorSignature, this.GetFirstLocation(), this.ContainingType);
            }

            CheckReturnIsNotVoid(diagnostics);
        }

        public sealed override string Name
        {
            get
            {
                return _name;
            }
        }

        public sealed override ImmutableArray<TypeParameterSymbol> TypeParameters
        {
            get { return ImmutableArray<TypeParameterSymbol>.Empty; }
        }

        public sealed override ImmutableArray<ImmutableArray<TypeWithAnnotations>> GetTypeParameterConstraintTypes()
            => ImmutableArray<ImmutableArray<TypeWithAnnotations>>.Empty;

        public sealed override ImmutableArray<TypeParameterConstraintKind> GetTypeParameterConstraintKinds()
            => ImmutableArray<TypeParameterConstraintKind>.Empty;

        protected sealed override void CheckConstraintsForExplicitInterfaceType(ConversionsBase conversions, BindingDiagnosticBag diagnostics)
        {
            if ((object)_explicitInterfaceType != null)
            {
                NameSyntax name;

                switch (syntaxReferenceOpt.GetSyntax())
                {
                    case OperatorDeclarationSyntax operatorDeclaration:
                        Debug.Assert(operatorDeclaration.ExplicitInterfaceSpecifier != null);
                        name = operatorDeclaration.ExplicitInterfaceSpecifier.Name;
                        break;

                    case ConversionOperatorDeclarationSyntax conversionDeclaration:
                        Debug.Assert(conversionDeclaration.ExplicitInterfaceSpecifier != null);
                        name = conversionDeclaration.ExplicitInterfaceSpecifier.Name;
                        break;

                    default:
                        throw ExceptionUtilities.Unreachable();
                }

                _explicitInterfaceType.CheckAllConstraints(DeclaringCompilation, conversions, new SourceLocation(name), diagnostics);
            }
        }

        protected sealed override void PartialMethodChecks(BindingDiagnosticBag diagnostics)
        {
        }
    }
}
