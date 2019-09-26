﻿// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Roslynator.CSharp.SyntaxWalkers;

namespace Roslynator.CSharp.Analysis
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class RemoveOriginalExceptionFromThrowStatementAnalyzer : BaseDiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(DiagnosticDescriptors.RemoveOriginalExceptionFromThrowStatement); }
        }

        public override void Initialize(AnalysisContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            base.Initialize(context);

            context.RegisterSyntaxNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);
        }

        public static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
        {
            var catchClause = (CatchClauseSyntax)context.Node;

            CatchDeclarationSyntax declaration = catchClause.Declaration;

            if (declaration == null)
                return;

            SemanticModel semanticModel = context.SemanticModel;
            CancellationToken cancellationToken = context.CancellationToken;

            ILocalSymbol symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);

            if (symbol?.IsErrorType() != false)
                return;

            Walker walker = Walker.GetInstance();

            walker.Symbol = symbol;
            walker.SemanticModel = semanticModel;
            walker.CancellationToken = cancellationToken;

            walker.VisitBlock(catchClause.Block);

            ExpressionSyntax expression = walker.ThrowStatement?.Expression;

            walker.Symbol = null;
            walker.SemanticModel = null;
            walker.CancellationToken = default;
            walker.ThrowStatement = null;

            Walker.Free(walker);

            if (expression != null)
            {
                DiagnosticHelpers.ReportDiagnostic(context,
                    DiagnosticDescriptors.RemoveOriginalExceptionFromThrowStatement,
                    expression);
            }
        }

        private class Walker : CSharpSyntaxNodeWalker
        {
            [ThreadStatic]
            private static Walker _cachedInstance;

            public ThrowStatementSyntax ThrowStatement { get; set; }

            public ISymbol Symbol { get; set; }

            public SemanticModel SemanticModel { get; set; }

            public CancellationToken CancellationToken { get; set; }

            public override void VisitCatchClause(CatchClauseSyntax node)
            {
            }

            public override void VisitThrowStatement(ThrowStatementSyntax node)
            {
                ExpressionSyntax expression = node.Expression;

                if (expression != null)
                {
                    ISymbol symbol = SemanticModel.GetSymbol(expression, CancellationToken);

                    if (Symbol.Equals(symbol))
                        ThrowStatement = node;
                }

                base.VisitThrowStatement(node);
            }

            public static Walker GetInstance()
            {
                Walker walker = _cachedInstance;

                if (walker != null)
                {
                    _cachedInstance = null;
                    return walker;
                }

                return new Walker();
            }

            public static void Free(Walker walker)
            {
                _cachedInstance = walker;
            }
        }
    }
}
