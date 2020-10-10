﻿// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslynator.CSharp;
using Roslynator.Formatting.CodeFixes.CSharp;
using Roslynator.Formatting.CSharp;

namespace Roslynator.Formatting.CodeFixes
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LineIsTooLongCodeFixProvider))]
    [Shared]
    internal class LineIsTooLongCodeFixProvider : BaseCodeFixProvider
    {
        private const string Title = "Wrap line";

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIdentifiers.LineIsTooLong);

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode root = await context.GetSyntaxRootAsync().ConfigureAwait(false);

            TextSpan span = context.Span;
            Document document = context.Document;
            Diagnostic diagnostic = context.Diagnostics[0];
            string indentation = null;
            int maxLength = AnalyzerSettings.Current.MaxLineLength;
            int position = span.End;

            Dictionary<SyntaxKind, SyntaxNode> spans = null;

            while (position >= span.Start)
            {
                SyntaxToken token = root.FindToken(position);

                SyntaxNode node = token.Parent;

                for (; node?.SpanStart >= span.Start; node = node.Parent)
                {
                    SyntaxKind kind = node.Kind();

                    if (spans != null
                        && spans.TryGetValue(kind, out SyntaxNode node2)
                        && object.ReferenceEquals(node, node2))
                    {
                        continue;
                    }

                    if (kind == SyntaxKind.ArrowExpressionClause)
                    {
                        var expressionBody = (ArrowExpressionClauseSyntax)node;

                        SyntaxToken arrowToken = expressionBody.ArrowToken;
                        SyntaxToken previousToken = arrowToken.GetPreviousToken();

                        if (previousToken.SpanStart < span.Start)
                            continue;

                        bool addNewLineAfter = document.IsAnalyzerOptionEnabled(
                            AnalyzerOptions.AddNewLineAfterExpressionBodyArrowInsteadOfBeforeIt);

                        int wrapPosition = (addNewLineAfter) ? arrowToken.Span.End : previousToken.Span.End;
                        int start = (addNewLineAfter) ? expressionBody.Expression.SpanStart : arrowToken.SpanStart;
                        int longestLength = expressionBody.GetLastToken().GetNextToken().Span.End - start;

                        if (!CanWrapNode(expressionBody, wrapPosition, longestLength))
                            continue;

                        AddSpan(expressionBody);
                        break;
                    }
                    else if (kind == SyntaxKind.ParameterList)
                    {
                        if (node.Parent is AnonymousFunctionExpressionSyntax)
                            continue;

                        var parameterList = (ParameterListSyntax)node;

                        if (!CanWrapSeparatedList(parameterList.Parameters, parameterList.OpenParenToken.Span.End))
                            continue;

                        AddSpan(parameterList);
                        break;
                    }
                    else if (kind == SyntaxKind.BracketedParameterList)
                    {
                        var parameterList = (BracketedParameterListSyntax)node;

                        if (!CanWrapSeparatedList(parameterList.Parameters, parameterList.OpenBracketToken.Span.End))
                            continue;

                        AddSpan(parameterList);
                        break;
                    }
                    else if (kind == SyntaxKind.ArgumentList)
                    {
                        var argumentList = (ArgumentListSyntax)node;

                        if (!CanWrapSeparatedList(argumentList.Arguments, argumentList.OpenParenToken.Span.End))
                            continue;

                        if (!CanWrapLine(argumentList.Parent))
                            continue;

                        AddSpan(argumentList);
                        break;
                    }
                    else if (kind == SyntaxKind.SimpleMemberAccessExpression)
                    {
                        if (!node.IsParentKind(SyntaxKind.InvocationExpression, SyntaxKind.ElementAccessExpression))
                            continue;

                        var memberAccessExpression = (MemberAccessExpressionSyntax)node;
                        SyntaxToken dotToken = memberAccessExpression.OperatorToken;

                        if (!CanWrapNode(memberAccessExpression, dotToken.SpanStart, span.End - dotToken.SpanStart))
                            continue;

                        if (!CanWrapLine(memberAccessExpression))
                            continue;

                        AddSpan(memberAccessExpression);
                        break;
                    }
                    else if (kind == SyntaxKind.MemberBindingExpression)
                    {
                        if (!node.IsParentKind(SyntaxKind.InvocationExpression, SyntaxKind.ElementAccessExpression))
                            continue;

                        var memberBindingExpression = (MemberBindingExpressionSyntax)node;
                        SyntaxToken dotToken = memberBindingExpression.OperatorToken;

                        if (!CanWrapNode(memberBindingExpression, dotToken.SpanStart, span.End - dotToken.SpanStart))
                            continue;

                        if (!CanWrapLine(memberBindingExpression))
                            continue;

                        AddSpan(memberBindingExpression);
                        break;
                    }
                    else if (kind == SyntaxKind.ConditionalExpression)
                    {
                        //TODO: 
                        break;
                    }

                    switch (kind)
                    {
                        case SyntaxKind.ArrayInitializerExpression:
                        case SyntaxKind.CollectionInitializerExpression:
                        case SyntaxKind.ComplexElementInitializerExpression:
                        case SyntaxKind.ObjectInitializerExpression:
                            {
                                var initializer = (InitializerExpressionSyntax)node;

                                if (!CanWrapSeparatedList(initializer.Expressions, initializer.OpenBraceToken.Span.End))
                                    continue;

                                AddSpan(initializer);
                                break;
                            }
                        case SyntaxKind.AddExpression:
                        case SyntaxKind.SubtractExpression:
                        case SyntaxKind.MultiplyExpression:
                        case SyntaxKind.DivideExpression:
                        case SyntaxKind.ModuloExpression:
                        case SyntaxKind.LeftShiftExpression:
                        case SyntaxKind.RightShiftExpression:
                        case SyntaxKind.LogicalOrExpression:
                        case SyntaxKind.LogicalAndExpression:
                        case SyntaxKind.BitwiseOrExpression:
                        case SyntaxKind.BitwiseAndExpression:
                        case SyntaxKind.ExclusiveOrExpression:
                        case SyntaxKind.CoalesceExpression:
                            {
                                var binaryExpression = (BinaryExpressionSyntax)node;

                                SyntaxToken operatorToken = binaryExpression.OperatorToken;

                                bool addNewLineAfter = document.IsAnalyzerOptionEnabled(
                                    AnalyzerOptions.AddNewLineAfterBinaryOperatorInsteadOfBeforeIt);

                                int wrapPosition = (addNewLineAfter)
                                    ? operatorToken.Span.End
                                    : binaryExpression.Left.Span.End;

                                int start = (addNewLineAfter) ? binaryExpression.Right.SpanStart : operatorToken.SpanStart;
                                int longestLength = span.End - start;

                                if (!CanWrapNode(binaryExpression, wrapPosition, longestLength))
                                    continue;

                                AddSpan(binaryExpression);
                                break;
                            }
                    }
                }

                position = Math.Min(position, token.FullSpan.Start) - 1;
            }

            if (spans == null)
                return;

            SyntaxNode nodeToFix = FindNodeToFix()
                ?? spans
                    .Select(f => f.Value)
                    .OrderBy(f => f, SyntaxKindComparer.Instance)
                    .First();

            CodeAction codeAction = CodeAction.Create(
                Title,
                GetCreateChangedDocument(nodeToFix),
                base.GetEquivalenceKey(diagnostic));

            context.RegisterCodeFix(codeAction, diagnostic);
            return;

            void AddSpan(SyntaxNode node)
            {
                SyntaxKind kind = node.Kind();

                if (spans == null)
                    spans = new Dictionary<SyntaxKind, SyntaxNode>();

                if (!spans.ContainsKey(kind))
                    spans[kind] = node;
            }

            SyntaxNode FindNodeToFix()
            {
                if (!spans.ContainsKey(SyntaxKind.ArgumentList))
                    return null;

                if (!spans.ContainsKey(SyntaxKind.SimpleMemberAccessExpression)
                    && !spans.ContainsKey(SyntaxKind.MemberBindingExpression))
                {
                    return null;
                }

                SyntaxNode argumentList = spans[SyntaxKind.ArgumentList];

                SyntaxNode memberExpression = null;

                SyntaxNode memberAccess = (spans.ContainsKey(SyntaxKind.SimpleMemberAccessExpression))
                    ? spans[SyntaxKind.SimpleMemberAccessExpression]
                    : null;

                SyntaxNode memberBinding = (spans.ContainsKey(SyntaxKind.MemberBindingExpression))
                    ? spans[SyntaxKind.MemberBindingExpression]
                    : null;

                if (memberAccess != null)
                {
                    if (memberBinding != null)
                    {
                        if (memberAccess.Contains(memberBinding))
                        {
                            memberExpression = memberAccess;
                        }
                        else if (memberBinding.Contains(memberAccess))
                        {
                            memberExpression = memberBinding;
                        }
                        else if (memberAccess.SpanStart > memberBinding.SpanStart)
                        {
                            memberExpression = memberBinding;
                        }
                        else
                        {
                            memberExpression = memberAccess;
                        }
                    }
                    else
                    {
                        memberExpression = memberAccess;
                    }
                }
                else
                {
                    memberExpression = memberBinding;
                }

                if (memberExpression.Span.End == argumentList.SpanStart)
                {
                    ExpressionSyntax expression = null;

                    if (memberExpression.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    {
                        var memberAccess2 = (MemberAccessExpressionSyntax)memberExpression;
                        expression = memberAccess2.Expression;
                    }
                    else
                    {
                        var memberBinding2 = (MemberBindingExpressionSyntax)memberExpression;

                        if (memberBinding2.Parent is ConditionalAccessExpressionSyntax conditionalAccess)
                            expression = conditionalAccess.Expression;
                    }

                    if (expression is SimpleNameSyntax)
                        return argumentList;

                    if (expression is CastExpressionSyntax castExpression
                        && castExpression.Expression is SimpleNameSyntax)
                    {
                        return argumentList;
                    }
                }

                if (argumentList.Contains(memberExpression))
                    return argumentList;

                if (memberExpression.Contains(argumentList))
                    return memberExpression;

                if (argumentList.SpanStart > memberExpression.SpanStart)
                    return memberExpression;

                return argumentList;
            }

            bool CanWrapSeparatedList<TNode>(
                SeparatedSyntaxList<TNode> nodes,
                int wrapPosition) where TNode : SyntaxNode
            {
                if (!nodes.Any())
                    return false;

                int longestLength = nodes.Max(f => f.Span.Length);

                return CanWrapNode(nodes.First(), wrapPosition, longestLength);
            }

            bool CanWrapNode(
                SyntaxNode node,
                int wrapPosition,
                int longestLength)
            {
                if (wrapPosition - span.Start > maxLength)
                    return false;

                indentation = SyntaxTriviaAnalysis.GetIncreasedIndentation(node);

                return indentation.Length + longestLength <= maxLength;
            }

            static bool CanWrapLine(SyntaxNode node)
            {
                for (SyntaxNode n = node; n != null; n = n.Parent)
                {
                    switch (n)
                    {
                        case MemberDeclarationSyntax _:
                        case StatementSyntax _:
                        case AccessorDeclarationSyntax _:
                            return true;
                        case InterpolationSyntax _:
                            return false;
                    }
                }

                return true;
            }

            Func<CancellationToken, Task<Document>> GetCreateChangedDocument(SyntaxNode node)
            {
                switch (node.Kind())
                {
                    case SyntaxKind.ArrowExpressionClause:
                        return ct => AddNewLineBeforeOrAfterArrowAsync(document, (ArrowExpressionClauseSyntax)node, ct);
                    case SyntaxKind.ParameterList:
                        return ct => SyntaxFormatter.WrapParametersAsync(document, (ParameterListSyntax)node, ct);
                    case SyntaxKind.BracketedParameterList:
                        return ct => SyntaxFormatter.WrapParametersAsync(document, (BracketedParameterListSyntax)node, ct);
                    case SyntaxKind.SimpleMemberAccessExpression:
                        return ct => SyntaxFormatter.WrapCallChainAsync(document, (MemberAccessExpressionSyntax)node, ct);
                    case SyntaxKind.MemberBindingExpression:
                        return ct => SyntaxFormatter.WrapCallChainAsync(document, (MemberBindingExpressionSyntax)node, ct);
                    case SyntaxKind.ArgumentList:
                        return ct => SyntaxFormatter.WrapArgumentsAsync(document, (ArgumentListSyntax)node, ct);
                    case SyntaxKind.ArrayInitializerExpression:
                        return ct => SyntaxFormatter.ToMultiLineAsync(document, (InitializerExpressionSyntax)node, ct);
                    case SyntaxKind.CollectionInitializerExpression:
                    case SyntaxKind.ComplexElementInitializerExpression:
                    case SyntaxKind.ObjectInitializerExpression:
                    case SyntaxKind.AddExpression:
                    case SyntaxKind.SubtractExpression:
                    case SyntaxKind.MultiplyExpression:
                    case SyntaxKind.DivideExpression:
                    case SyntaxKind.ModuloExpression:
                    case SyntaxKind.LeftShiftExpression:
                    case SyntaxKind.RightShiftExpression:
                    case SyntaxKind.LogicalOrExpression:
                    case SyntaxKind.LogicalAndExpression:
                    case SyntaxKind.BitwiseOrExpression:
                    case SyntaxKind.BitwiseAndExpression:
                    case SyntaxKind.ExclusiveOrExpression:
                    case SyntaxKind.CoalesceExpression:
                        return ct => AddNewLineBeforeOrAfterBinaryOperatorAsync(document, (BinaryExpressionSyntax)node, ct);
                    default:
                        return null;
                }
            }
        }

        private static Task<Document> AddNewLineBeforeOrAfterArrowAsync(
            Document document,
            ArrowExpressionClauseSyntax arrowExpressionClause,
            CancellationToken cancellationToken = default)
        {
            return AddNewLineBeforeOrAfterAsync(
                document,
                arrowExpressionClause.ArrowToken,
                document.IsAnalyzerOptionEnabled(
                    AnalyzerOptions.AddNewLineAfterExpressionBodyArrowInsteadOfBeforeIt),
                cancellationToken);
        }

        private static Task<Document> AddNewLineBeforeOrAfterBinaryOperatorAsync(
            Document document,
            BinaryExpressionSyntax binaryExpression,
            CancellationToken cancellationToken = default)
        {
            return AddNewLineBeforeOrAfterAsync(
                document,
                binaryExpression.OperatorToken,
                document.IsAnalyzerOptionEnabled(
                    AnalyzerOptions.AddNewLineAfterBinaryOperatorInsteadOfBeforeIt),
                cancellationToken);
        }

        private static Task<Document> AddNewLineBeforeOrAfterAsync(
            Document document,
            SyntaxToken token,
            bool addNewLineAfter,
            CancellationToken cancellationToken = default)
        {
            string indentation = SyntaxTriviaAnalysis.GetIncreasedIndentation(token.Parent, cancellationToken);

            return (addNewLineAfter)
                ? CodeFixHelpers.AddNewLineAfterAsync(document, token, indentation, cancellationToken)
                : CodeFixHelpers.AddNewLineBeforeAsync(document, token, indentation, cancellationToken);
        }

        private class SyntaxKindComparer : IComparer<SyntaxNode>
        {
            public static SyntaxKindComparer Instance { get; } = new SyntaxKindComparer();

            public int Compare(SyntaxNode x, SyntaxNode y)
            {
                if (object.ReferenceEquals(x, y))
                    return 0;

                if (x == null)
                    return -1;

                if (y == null)
                    return 1;

                return GetRank(x.Kind()).CompareTo(GetRank(y.Kind()));
            }

            private static int GetRank(SyntaxKind kind)
            {
                switch (kind)
                {
                    case SyntaxKind.ArrowExpressionClause:
                        return 10;
                    case SyntaxKind.ArrayInitializerExpression:
                    case SyntaxKind.CollectionInitializerExpression:
                    case SyntaxKind.ComplexElementInitializerExpression:
                    case SyntaxKind.ObjectInitializerExpression:
                        return 11;
                    case SyntaxKind.ParameterList:
                        return 20;
                    case SyntaxKind.BracketedParameterList:
                        return 30;
                    case SyntaxKind.AddExpression:
                    case SyntaxKind.SubtractExpression:
                    case SyntaxKind.MultiplyExpression:
                    case SyntaxKind.DivideExpression:
                    case SyntaxKind.ModuloExpression:
                    case SyntaxKind.LeftShiftExpression:
                    case SyntaxKind.RightShiftExpression:
                    case SyntaxKind.LogicalOrExpression:
                    case SyntaxKind.LogicalAndExpression:
                    case SyntaxKind.BitwiseOrExpression:
                    case SyntaxKind.BitwiseAndExpression:
                    case SyntaxKind.ExclusiveOrExpression:
                    case SyntaxKind.CoalesceExpression:
                        return 31;
                    case SyntaxKind.SimpleMemberAccessExpression:
                        return 40;
                    case SyntaxKind.MemberBindingExpression:
                        return 50;
                    case SyntaxKind.ArgumentList:
                        return 60;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }
    }
}

