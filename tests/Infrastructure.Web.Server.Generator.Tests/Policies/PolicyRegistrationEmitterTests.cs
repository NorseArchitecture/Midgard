using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Norse.Infrastructure.Web.Server.Generator.Policies;

namespace Norse.Infrastructure.Web.Server.Generator.Tests.Policies;

public sealed class PolicyRegistrationEmitterTests
{
	static readonly ImmutableArray<PolicyDeclaration> _two =
	[
		new("AuthN.Public", "Norse.AuthN.Services.AuthNPolicies", "Public"),
		new("Reference.Public", "Norse.Reference.ReferencePolicies", "Public")
	];

	[Fact]
	void Emits_a_registration_per_declaration()
	{
		var emitted = PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", _two);

		emitted.ShouldContain("""AddPolicy("AuthN.Public", global::Norse.AuthN.Services.AuthNPolicies.Public)""");
		emitted.ShouldContain("""AddPolicy("Reference.Public", global::Norse.Reference.ReferencePolicies.Public)""");
	}

	[Fact]
	void Emits_into_the_consuming_assemblys_namespace() =>
		PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", []).ShouldContain("namespace Norse.Hosting.Web.Server;");

	[Fact]
	void Emits_lf_only_with_no_bom()
	{
		var emitted = PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", _two);

		emitted.ShouldNotContain("\r");
		emitted[0].ShouldNotBe('﻿');
	}

	[Fact]
	void Emits_a_compiling_shape_with_no_declarations() =>
		PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", []).ShouldContain("AddNorsePolicies");

	[Fact]
	void Orders_declarations_deterministically()
	{
		var forward = PolicyRegistrationEmitter.Emit("N", _two);
		var reversed = PolicyRegistrationEmitter.Emit("N", [_two[1], _two[0]]);

		reversed.ShouldBe(forward);
	}

	[Theory]
	[InlineData("""Realm."Quoted".Policy""")]
	[InlineData(@"Realm\Backslash")]
	[InlineData("Realm\nNewline")]
	[InlineData("RealmBell")]
	void Escapes_hostile_policy_names_into_valid_csharp(string name)
	{
		var emitted = PolicyRegistrationEmitter.Emit("N",
			[new PolicyDeclaration(name, "A.B", "Configure")]);

		// Parsed, not pattern-matched: the only question that matters is whether the emitted file compiles.
		SyntaxFactory.ParseCompilationUnit(emitted).GetDiagnostics()
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ShouldBeEmpty();
	}
}
