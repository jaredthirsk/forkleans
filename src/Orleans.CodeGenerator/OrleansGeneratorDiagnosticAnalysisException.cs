using Microsoft.CodeAnalysis;
using System;

namespace Forkleans.CodeGenerator
{
    public class OrleansGeneratorDiagnosticAnalysisException : Exception
    {
        public OrleansGeneratorDiagnosticAnalysisException(Diagnostic diagnostic) : base(diagnostic.GetMessage())
        {
            Diagnostic = diagnostic;
        }

        public Diagnostic Diagnostic { get; }
    }
}
