using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Norse.Infrastructure.Web.Server.Generator.Policies;

namespace Norse.Infrastructure.Web.Server.Generator.Tests.Policies;

public sealed class PolicyRegistrationEmitterTests
{
	const string Bootstrap = "services.AddAuthorizationBuilder()";

	static readonly ImmutableArray<PolicyDeclaration> _two =
	[
		new("AuthN.Public", "Norse.AuthN.Services.AuthNPolicies", "Public"),
		new("Reference.Public", "Norse.Reference.ReferencePolicies", "Public")
	];

	[Fact]
	void Emits_a_registration_per_declaration()
	{
		var emitted = PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", Bootstrap, _two);

		emitted.ShouldContain("""AddPolicy("AuthN.Public", static policy => { global::Norse.AuthN.Services.AuthNPolicies.Public(policy);""");
		emitted.ShouldContain("""AddPolicy("Reference.Public", static policy => { global::Norse.Reference.ReferencePolicies.Public(policy);""");
	}

	// Snapshot of the registration block: every entry runs its declared configure method, then closes the lane
	// by default (spec 7.5), named by its containing type so the generated file assumes no using.
	[Fact]
	void Every_registration_closes_the_lane_by_default_after_its_configure_method()
	{
		var emitted = PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", Bootstrap, _two);

		emitted.ShouldContain("""
					var builder = services.AddAuthorizationBuilder();
					builder.AddPolicy("AuthN.Public", static policy => { global::Norse.AuthN.Services.AuthNPolicies.Public(policy); global::Norse.Abstractions.Components.Authorization.NorseLanePolicyExtensions.CloseLaneByDefault(policy); });
					builder.AddPolicy("Reference.Public", static policy => { global::Norse.Reference.ReferencePolicies.Public(policy); global::Norse.Abstractions.Components.Authorization.NorseLanePolicyExtensions.CloseLaneByDefault(policy); });
					return services;
			""".Replace("\r", string.Empty));
	}

	[Fact]
	void Emits_into_the_consuming_assemblys_namespace() =>
		PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", Bootstrap, []).ShouldContain("namespace Norse.Hosting.Web.Server;");

	[Fact]
	void Emits_lf_only_with_no_bom()
	{
		var emitted = PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", Bootstrap, _two);

		emitted.ShouldNotContain("\r");
		emitted[0].ShouldNotBe('﻿');
	}

	[Fact]
	void Emits_a_compiling_shape_with_no_declarations() =>
		PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", Bootstrap, []).ShouldContain("AddNorsePolicies");

	[Fact]
	void Orders_declarations_deterministically()
	{
		var forward = PolicyRegistrationEmitter.Emit("N", Bootstrap, _two);
		var reversed = PolicyRegistrationEmitter.Emit("N", Bootstrap, [_two[1], _two[0]]);

		reversed.ShouldBe(forward);
	}

	[Theory]
	[InlineData("""Realm."Quoted".Policy""")]
	[InlineData(@"Realm\Backslash")]
	[InlineData("Realm\nNewline")]
	[InlineData("RealmBell")]
	void Escapes_hostile_policy_names_into_valid_csharp(string name)
	{
		var emitted = PolicyRegistrationEmitter.Emit("N", Bootstrap,
			[new PolicyDeclaration(name, "A.B", "Configure")]);

		// Parsed, not pattern-matched: the only question that matters is whether the emitted file compiles.
		SyntaxFactory.ParseCompilationUnit(emitted).GetDiagnostics()
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ShouldBeEmpty();
	}
}
