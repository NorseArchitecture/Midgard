using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     The library-controller tripwire (spec §3, ratified 2026-08-02): at application startup — before the
///     host ever serves a request, never as a runtime 500 — enumerates every discovered
///     <c>GrpcControllerBase</c> descendant via <see cref="ApplicationPartManager" />'s
///     <see cref="ControllerFeature" /> and asserts every body-bound action-parameter type and
///     <c>ActionResult&lt;T&gt;</c> payload type carries a shape in the registry. This is the platform's
///     answer to "shipped a reusable controller library, the generator silently generated nothing for it" —
///     the same failure mode that once left <c>OutcomeServerInterceptor</c> implemented and unit-tested but
///     never wired.
/// </summary>
/// <remarks>
///     <c>Norse.Abstractions.Web.Server.Facade.GrpcControllerBase</c> does not exist on the platform yet
///     (Task 10, Asgard, a different repo, dispatched later) — this filter therefore matches it by its
///     fully-qualified name walked reflectively up the base-type chain, never a compile-time type
///     reference, mirroring exactly how <c>XmlShapeGenerator</c> keys on the same string via
///     <c>Compilation.GetTypeByMetadataName</c> at the Roslyn level. Once Asgard ships the real type, this
///     string match keeps working unmodified.
/// </remarks>
sealed class XmlShapeTripwireStartupFilter : IStartupFilter
{
	const string GrpcControllerBaseFullName = "Norse.Abstractions.Web.Server.Facade.GrpcControllerBase";

	readonly XmlShapeRegistry _registry;

	public XmlShapeTripwireStartupFilter(XmlShapeRegistry registry) =>
		_registry = registry;

	/// <inheritdoc />
	public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
	{
		Validate(app.ApplicationServices);
		next(app);
	};

	[UnconditionalSuppressMessage("Trimming", "IL2075",
		Justification =
			"Controller types come from ApplicationPartManager's own ControllerFeature over the host's compiled assemblies — MVC's controller-discovery machinery already requires those types (and their public action methods) survive trimming, or MVC itself couldn't route to them; reflecting over the same types to enforce this tripwire adds no new trim risk.")]
	void Validate(IServiceProvider services)
	{
		var partManager = services.GetRequiredService<ApplicationPartManager>();
		ControllerFeature feature = new();
		partManager.PopulateFeature(feature);

		foreach (var controllerType in feature.Controllers)
		{
			if (!DerivesFromGrpcControllerBase(controllerType))
				continue;

			foreach (var method in controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance |
				BindingFlags.DeclaredOnly))
			{
				if (method.IsSpecialName || method.GetCustomAttribute<NonActionAttribute>() is not null)
					continue;

				var parameters = method.GetParameters();
				foreach (var parameter in parameters)
					if (IsBodyBound(parameter, parameters.Length))
						EnsureShape(controllerType, parameter.ParameterType);

				if (TryGetActionResultPayload(method.ReturnType) is { } payloadType)
					EnsureShape(controllerType, payloadType);
			}
		}
	}

	/// <summary>
	///     Mirrors <c>ClosureWalker.Analyze</c>'s own request-root rule (Task 5/6,
	///     <c>Xml.Generator/ClosureWalker.cs</c>, the parameter loop feeding <c>requestRoots.Add</c>) at
	///     the runtime/reflection layer: a parameter is body-bound if it carries an explicit
	///     <c>[FromBody]</c>, OR — the implicit-binding convention MVC itself supports and the generator
	///     already treats as body-bound — it carries none of the other explicit binding-source attributes,
	///     is the method's only parameter, and its type falls outside the closed scalar taxonomy.
	///     Independently maintained by design (compile-time Roslyn symbols vs. runtime reflection — no
	///     code can be shared between the two), but the RULE must track <c>ClosureWalker</c>'s exactly: a
	///     change there needs its mirror updated here too, the same discipline
	///     <see cref="DerivesFromGrpcControllerBase" /> already applies to the metadata-name string match.
	/// </summary>
	static bool IsBodyBound(ParameterInfo parameter, int parameterCount)
	{
		if (parameter.GetCustomAttribute<FromBodyAttribute>() is not null)
			return true;

		var hasExplicitOtherSource =
			parameter.GetCustomAttribute<FromRouteAttribute>() is not null ||
			parameter.GetCustomAttribute<FromQueryAttribute>() is not null ||
			parameter.GetCustomAttribute<FromHeaderAttribute>() is not null ||
			parameter.GetCustomAttribute<FromServicesAttribute>() is not null;

		return !hasExplicitOtherSource && parameterCount == 1 && !IsSupportedScalar(parameter.ParameterType);
	}

	/// <summary>
	///     Mirrors <c>ClosureWalker.IsSupportedScalar</c>/<c>IsKnownScalarStruct</c> — the same closed taxonomy, checked
	///     reflectively instead of via <c>ITypeSymbol</c>.
	/// </summary>
	static bool IsSupportedScalar(Type type)
	{
		if (type.IsEnum)
			return true;

		if (type == typeof(bool) || type == typeof(sbyte) || type == typeof(byte) ||
			type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
			type == typeof(long) || type == typeof(ulong) || type == typeof(decimal) ||
			type == typeof(float) || type == typeof(double) || type == typeof(char) || type == typeof(string))
			return true;

		return type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
			type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan);
	}

	void EnsureShape(TypeInfo controllerType, Type memberType)
	{
		if (!_registry.TryGet(memberType, out _))
			throw new InvalidOperationException(
				$"facade controllers are host-compilation source — '{controllerType.Name}' exposes '{memberType.Name}' with no generated shape; controllers shipped in referenced assemblies generate nothing");
	}

	static bool DerivesFromGrpcControllerBase(TypeInfo type)
	{
		for (var current = type.BaseType; current is not null; current = current.BaseType)
			if (string.Equals(current.FullName, GrpcControllerBaseFullName, StringComparison.Ordinal))
				return true;

		return false;
	}

	static Type? TryGetActionResultPayload(Type returnType)
	{
		if (TryUnwrapActionResult(returnType, out var payload))
			return payload;

		if (returnType.IsGenericType)
		{
			var definition = returnType.GetGenericTypeDefinition();
			if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
			{
				var inner = returnType.GetGenericArguments()[0];
				if (TryUnwrapActionResult(inner, out var innerPayload))
					return innerPayload;
			}
		}

		return null;
	}

	static bool TryUnwrapActionResult(Type type, out Type payload)
	{
		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ActionResult<>))
		{
			payload = type.GetGenericArguments()[0];
			return true;
		}

		payload = null!;
		return false;
	}
}
