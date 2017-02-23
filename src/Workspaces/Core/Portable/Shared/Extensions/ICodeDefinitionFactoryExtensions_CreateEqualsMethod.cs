﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Shared.Extensions
{
    internal static partial class ICodeDefinitionFactoryExtensions
    {
        private const string EqualsName = "Equals";
        private const string DefaultName = "Default";
        private const string ObjName = "obj";

        public static IMethodSymbol CreateEqualsMethod(
            this SyntaxGenerator factory,
            Compilation compilation,
            INamedTypeSymbol containingType,
            IList<ISymbol> symbols,
            CancellationToken cancellationToken)
        {
            var statements = CreateEqualsMethodStatements(factory, compilation, containingType, symbols, cancellationToken);

            return CodeGenerationSymbolFactory.CreateMethodSymbol(
                attributes: null,
                accessibility: Accessibility.Public,
                modifiers: new DeclarationModifiers(isOverride: true),
                returnType: compilation.GetSpecialType(SpecialType.System_Boolean),
                returnsByRef: false,
                explicitInterfaceSymbol: null,
                name: EqualsName,
                typeParameters: null,
                parameters: new[] { CodeGenerationSymbolFactory.CreateParameterSymbol(compilation.GetSpecialType(SpecialType.System_Object), ObjName) },
                statements: statements);
        }

        private static IList<SyntaxNode> CreateEqualsMethodStatements(
            SyntaxGenerator factory,
            Compilation compilation,
            INamedTypeSymbol containingType,
            IEnumerable<ISymbol> members,
            CancellationToken cancellationToken)
        {
            var iequatableType = compilation.GetTypeByMetadataName("System.IEquatable`1");
            var statements = new List<SyntaxNode>();

            var parts = StringBreaker.BreakIntoWordParts(containingType.Name);
            string localName = "v";
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i];
                if (char.IsLetter(containingType.Name[p.Start]))
                {
                    localName = containingType.Name.Substring(p.Start, p.Length).ToCamelCase();
                    break;
                }
            }

            var localNameExpression = factory.IdentifierName(localName);

            var objNameExpression = factory.IdentifierName(ObjName);

            var expressions = new List<SyntaxNode>();

            if (containingType.IsValueType)
            {
#if false
                if (!(obj is MyType))
                {
                    return false;
                }
#endif
                var ifStatement = factory.IfStatement(
                    factory.LogicalNotExpression(
                        factory.IsTypeExpression(
                            objNameExpression,
                            containingType)),
                    new[] { factory.ReturnStatement(factory.FalseLiteralExpression()) });

#if false
                var myType = (MyType)obj;
#endif
                var localDeclaration = factory.LocalDeclarationStatement(localName, factory.CastExpression(containingType, objNameExpression));

                statements.Add(ifStatement);
                statements.Add(localDeclaration);
            }
            else
            {
#if false
                var myType = obj as MyType;
#endif
                var localDeclaration = factory.LocalDeclarationStatement(localName, factory.TryCastExpression(objNameExpression, containingType));

                statements.Add(localDeclaration);

#if false
                myType != null
#endif
                expressions.Add(factory.ReferenceNotEqualsExpression(localNameExpression, factory.NullLiteralExpression()));
                if (HasExistingBaseEqualsMethod(containingType, cancellationToken))
                {
#if false
                    base.Equals(obj)
#endif
                    expressions.Add(factory.InvocationExpression(
                        factory.MemberAccessExpression(
                            factory.BaseExpression(),
                            factory.IdentifierName(EqualsName)),
                        objNameExpression));
                }
            }

            foreach (var member in members)
            {
                var symbolNameExpression = factory.IdentifierName(member.Name);
                var thisSymbol = factory.MemberAccessExpression(factory.ThisExpression(), symbolNameExpression).WithAdditionalAnnotations(Simplification.Simplifier.Annotation);
                var otherSymbol = factory.MemberAccessExpression(localNameExpression, symbolNameExpression);

#if false
                EqualityComparer<SType>.Default.Equals(this.S1, myType.S1)
#endif
                var memberType = member.GetSymbolType();

                if (IsPrimitiveValueType(memberType))
                {
                    var expression = factory.ValueEqualsExpression(thisSymbol, otherSymbol);
                    expressions.Add(expression);
                }
                else if (memberType?.IsValueType == true &&
                         ImplementsIEquatable(memberType, iequatableType))
                {
                    var expression = factory.InvocationExpression(
                        factory.MemberAccessExpression(thisSymbol, nameof(object.Equals)),
                        otherSymbol);
                    expressions.Add(expression);
                }
                else
                {
                    var expression =
                        factory.InvocationExpression(
                            factory.MemberAccessExpression(
                                GetDefaultEqualityComparer(factory, compilation, member),
                                factory.IdentifierName(EqualsName)),
                            thisSymbol,
                            otherSymbol);

                    expressions.Add(expression);
                }
            }

#if false
            return myType != null && base.Equals(obj) && EqualityComparer<int>.Default.Equals(this.S1, myType.S1) && ...;
#endif
            statements.Add(factory.ReturnStatement(
                expressions.Aggregate(factory.LogicalAndExpression)));

            return statements;
        }

        private static bool ImplementsIEquatable(ITypeSymbol memberType, INamedTypeSymbol iequatableType)
        {
            if (iequatableType != null) {
                var constructed = iequatableType.Construct(memberType);
                return memberType.AllInterfaces.Any(t => t.OriginalDefinition.Equals(constructed));
            }

            return false;
        }

        private static bool IsPrimitiveValueType(ITypeSymbol typeSymbol)
        {
            if (typeSymbol != null)
            {
                if (typeSymbol.IsEnumType())
                {
                    return true;
                }

                switch (typeSymbol.SpecialType)
                {
                    case SpecialType.System_Boolean:
                    case SpecialType.System_Char:
                    case SpecialType.System_SByte:
                    case SpecialType.System_Byte:
                    case SpecialType.System_Int16:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_Int32:
                    case SpecialType.System_UInt32:
                    case SpecialType.System_Int64:
                    case SpecialType.System_UInt64:
                    case SpecialType.System_Decimal:
                    case SpecialType.System_Single:
                    case SpecialType.System_Double:
                    case SpecialType.System_String:
                    case SpecialType.System_Nullable_T:
                    case SpecialType.System_DateTime:
                        return true;
                }
            }

            return false;
        }

        private static SyntaxNode GetDefaultEqualityComparer(
            SyntaxGenerator factory,
            Compilation compilation,
            ISymbol member)
        {
            var equalityComparerType = compilation.EqualityComparerOfTType();
            var constructedType = equalityComparerType.Construct(GetType(compilation, member));
            return factory.MemberAccessExpression(
                factory.TypeExpression(constructedType),
                factory.IdentifierName(DefaultName));
        }

        private static ITypeSymbol GetType(Compilation compilation, ISymbol symbol)
        {
            switch (symbol)
            {
                case IFieldSymbol field: return field.Type;
                case IPropertySymbol property: return property.Type;
                default: return compilation.GetSpecialType(SpecialType.System_Object);
            }
        }

        private static bool HasExistingBaseEqualsMethod(INamedTypeSymbol containingType, CancellationToken cancellationToken)
        {
            // Check if any of our base types override Equals.  If so, first check with them.
            var existingMethods =
                from baseType in containingType.GetBaseTypes()
                from method in baseType.GetMembers(EqualsName).OfType<IMethodSymbol>()
                where method.IsOverride &&
                      method.DeclaredAccessibility == Accessibility.Public &&
                      !method.IsStatic &&
                      method.Parameters.Length == 1 &&
                      method.ReturnType.SpecialType == SpecialType.System_Boolean &&
                      method.Parameters[0].Type.SpecialType == SpecialType.System_Object
                select method;

            return existingMethods.Any();
        }
    }
}
