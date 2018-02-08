﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Operations;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.InitializeParameter
{
    internal static class InitializeParameterHelpers
    {
        public static bool IsFunctionDeclaration(SyntaxNode node)
            => node is BaseMethodDeclarationSyntax
            || node is LocalFunctionStatementSyntax
            || node is AnonymousFunctionExpressionSyntax;

        public static SyntaxNode GetBody(SyntaxNode functionDeclaration)
        {
            switch (functionDeclaration)
            {
                case BaseMethodDeclarationSyntax methodDeclaration:
                    return (SyntaxNode)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
                case LocalFunctionStatementSyntax localFunction:
                    return (SyntaxNode)localFunction.Body ?? localFunction.ExpressionBody;
                case AnonymousFunctionExpressionSyntax anonymousFunction:
                    return (SyntaxNode)anonymousFunction.Body;
                default:
                    throw ExceptionUtilities.UnexpectedValue(functionDeclaration);
            }
        }

        private static SyntaxToken? GetSemicolonToken(SyntaxNode functionDeclaration)
        {
            switch (functionDeclaration)
            {
                case BaseMethodDeclarationSyntax methodDeclaration:
                    return methodDeclaration.SemicolonToken;
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction.SemicolonToken;
                case AnonymousFunctionExpressionSyntax _:
                    return null;
                default:
                    throw ExceptionUtilities.UnexpectedValue(functionDeclaration);
            }
        }

        public static bool IsImplicitConversion(Compilation compilation, ITypeSymbol source, ITypeSymbol destination)
            => compilation.ClassifyConversion(source: source, destination: destination).IsImplicit;

        public static SyntaxNode TryGetLastStatement(IBlockOperation blockStatementOpt)
            => blockStatementOpt?.Syntax is BlockSyntax block
                ? block.Statements.LastOrDefault()
                : blockStatementOpt?.Syntax;

        public static void InsertStatement(
            SyntaxEditor editor,
            SyntaxNode functionDeclaration,
            IMethodSymbol method,
            SyntaxNode statementToAddAfterOpt,
            StatementSyntax statement)
        {
            var body = GetBody(functionDeclaration);

            if (IsExpressionBody(body))
            {
                var semicolonToken = GetSemicolonToken(functionDeclaration) ?? SyntaxFactory.Token(SyntaxKind.SemicolonToken);

                if (!TryConvertExpressionBodyToStatement(body, semicolonToken, !method.ReturnsVoid, out var convertedStatement))
                    return;

                // Add the new statement as the first/last statement of the new block 
                // depending if we were asked to go after something or not.
                editor.SetStatements(functionDeclaration, statementToAddAfterOpt == null
                    ? ImmutableArray.Create(statement, convertedStatement)
                    : ImmutableArray.Create(convertedStatement, statement));
            }
            else if (body is BlockSyntax block)
            {
                // Look for the statement we were asked to go after.
                var indexToAddAfter = block.Statements.IndexOf(s => s == statementToAddAfterOpt);
                if (indexToAddAfter >= 0)
                {
                    // If we find it, then insert the new statement after it.
                    editor.InsertAfter(block.Statements[indexToAddAfter], statement);
                }
                else if (block.Statements.Count > 0)
                {
                    // Otherwise, if we have multiple statements already, then insert ourselves
                    // before the first one.
                    editor.InsertBefore(block.Statements[0], statement);
                }
                else
                {
                    // Otherwise, we have no statements in this block.  Add the new statement
                    // as the single statement the block will have.
                    Debug.Assert(block.Statements.Count == 0);
                    editor.ReplaceNode(block, block.AddStatements(statement));
                }
            }
            else
            {
                editor.SetStatements(functionDeclaration, ImmutableArray.Create(statement));
            }
        }

        // either from an expression lambda or expression bodied member
        public static bool IsExpressionBody(SyntaxNode body)
            => body is ExpressionSyntax || body is ArrowExpressionClauseSyntax;

        public static bool TryConvertExpressionBodyToStatement(SyntaxNode body, SyntaxToken semicolonToken, bool createReturnStatementForExpression, out StatementSyntax statement)
        {
            Debug.Assert(IsExpressionBody(body));

            if (body is ArrowExpressionClauseSyntax arrowClause)
            {
                // If this is a => method, then we'll have to convert the method to have a block body.
                return arrowClause.TryConvertToStatement(semicolonToken, createReturnStatementForExpression, out statement);
            }
            else if (body is ExpressionSyntax expression)
            {
                // must be a lambda
                statement = ArrowExpressionClauseSyntaxExtensions.ConvertToStatement(expression, semicolonToken, createReturnStatementForExpression);
                return true;
            }

            statement = null;
            return false;
        }
    }
}
