using Microsoft.CodeAnalysis;

namespace Forkleans.CodeGenerator
{
    internal interface ICopierDescription
    {
        ITypeSymbol UnderlyingType { get; }
    }
}