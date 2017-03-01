﻿// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Roslynator.CSharp.Refactorings;
using Roslynator.Extensions;

namespace Roslynator.CSharp.DiagnosticAnalyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ForStatementDiagnosticAnalyzer : BaseDiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(
                    DiagnosticDescriptors.AvoidUsageOfForStatementToCreateInfiniteLoop,
                    DiagnosticDescriptors.RemoveRedundantBooleanLiteral);
            }
        }

        public override void Initialize(AnalysisContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.RegisterSyntaxNodeAction(f => AnalyzeForStatement(f), SyntaxKind.ForStatement);
        }

        private void AnalyzeForStatement(SyntaxNodeAnalysisContext context)
        {
            if (GeneratedCodeAnalyzer?.IsGeneratedCode(context) == true)
                return;

            var forStatement = (ForStatementSyntax)context.Node;

            AvoidUsageOfForStatementToCreateInfiniteLoopRefactoring.Analyze(context, forStatement);

            ExpressionSyntax condition = forStatement.Condition;

            if (condition?.IsKind(SyntaxKind.TrueLiteralExpression) == true)
                context.ReportDiagnostic(DiagnosticDescriptors.RemoveRedundantBooleanLiteral, condition, condition.ToString());
        }
    }
}
