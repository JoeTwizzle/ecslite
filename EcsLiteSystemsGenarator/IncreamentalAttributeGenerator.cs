using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace EcsLiteSystemsGenarator
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class EcsWriteAttribute : Attribute
    {
        // This is a positional argument
        public EcsWriteAttribute(params Type[]? types)
        {
            WrittenTypes = types?.Distinct() ?? Array.Empty<Type>();
        }

        public IEnumerable<Type> WrittenTypes { get; }
    }
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class EcsReadAttribute : Attribute
    {
        // This is a positional argument
        public EcsReadAttribute(params Type[]? types)
        {
            ReadTypes = types?.Distinct() ?? Array.Empty<Type>();
        }

        public IEnumerable<Type> ReadTypes { get; }
    }
    public interface IEcsRunSystem { void Run(); }
    [Generator]
    public class IncreamentalAttributeGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations =
                context.SyntaxProvider.CreateSyntaxProvider(
                    //The type of synatx we want to inspect
                    predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                    //actual inspection of syntax
                    transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
                .Where(static m => m is not null)!; //ClassDeclarationSyntax not null

            IncrementalValueProvider<(Compilation, ImmutableArray<ClassDeclarationSyntax>)> compilationAndClasses =
                context.CompilationProvider.Combine(classDeclarations.Collect());

            context.RegisterSourceOutput(compilationAndClasses,
                static (spc, source) => Execute(source.Item1, source.Item2, spc));
        }



        //Target methods that have attributes
        static bool IsSyntaxTargetForGeneration(SyntaxNode node)
        {
            return node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0;
        }

        const string LoggerMessageAttribute = "Microsoft.Extensions.Logging.LoggerMessageAttribute";
        static ClassDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
        {
            // we know the node is a MethodDeclarationSyntax thanks to IsSyntaxTargetForGeneration
            var methodDeclarationSyntax = (MethodDeclarationSyntax)context.Node;

            // loop through all the attributes on the method
            foreach (AttributeListSyntax attributeListSyntax in methodDeclarationSyntax.AttributeLists)
            {
                foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
                {
                    IMethodSymbol? attributeSymbol = context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol as IMethodSymbol;
                    if (attributeSymbol == null)
                    {
                        // weird, we couldn't get the symbol, ignore it
                        continue;
                    }

                    INamedTypeSymbol attributeContainingTypeSymbol = attributeSymbol.ContainingType;
                    string fullName = attributeContainingTypeSymbol.ToDisplayString();

                    // Is the attribute the [LoggerMessage] attribute?
                    if (fullName == LoggerMessageAttribute)
                    {
                        // return the parent class of the method
                        return methodDeclarationSyntax.Parent as ClassDeclarationSyntax;
                    }
                }
            }

            // we didn't find the attribute we were looking for
            return null;
        }
        private static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes, SourceProductionContext context)
        {
            if (classes.IsDefaultOrEmpty)
            {
                // nothing to do yet
                return;
            }

            IEnumerable<ClassDeclarationSyntax> distinctClasses = classes.Distinct();

            //var p = new Parser(compilation, context.ReportDiagnostic, context.CancellationToken);

            //IReadOnlyList<LoggerClass> logClasses = p.GetLogClasses(distinctClasses);
            //if (logClasses.Count > 0)
            //{
            //    var e = new Emitter();
            //    string result = e.Emit(logClasses, context.CancellationToken);

            //    context.AddSource("LoggerMessage.g.cs", SourceText.From(result, Encoding.UTF8));
            //}
        }
    }


    class MyClass : IEcsRunSystem
    {
        public void Run()
        {

        }
    }
}
