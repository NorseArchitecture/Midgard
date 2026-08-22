using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Norse.Abstractions.Components.Authorization;
using Norse.Abstractions.Web.Server.Facade;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     Layer 1 of scheme selection (design §2.2): decides a request's lane from <b>endpoint shape only</b>.
///     It reads no cookies, no headers, and invokes no handler, so it is result-blind and cannot recurse.
///     Everything credential-dependent belongs to <see cref="NorseBrowserHandler" />.
/// </summary>
static class NorseLaneSelector
{
	internal static string Select(Endpoint? endpoint)
	{
		if (endpoint is null)
			return NorseSchemes.Browser;

		if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action
			&& typeof(GrpcControllerBase).IsAssignableFrom(action.ControllerTypeInfo))
			return NorseSchemes.Machine;

		// Task 0's verdict decides this line. If GrpcMethodMetadata is present on code-first endpoints it is
		// the marker; otherwise NorseGrpcLaneMetadata added at MapGrpcService time is. The row's position and
		// behavior do not change either way -- only the type it matches on.
		// global:: is load-bearing here, not stylistic: this file's own namespace nests under
		// Norse.Infrastructure.Web, which shadows the unqualified "Grpc" segment with the sibling
		// Norse.Infrastructure.Web.Grpc project namespace and sends an unqualified reference there instead
		// of Grpc.AspNetCore.Server (CS0234) -- a mechanical fix, not a change in what this line matches.
		if (endpoint.Metadata.GetMetadata<global::Grpc.AspNetCore.Server.GrpcMethodMetadata>() is not null)
			return NorseSchemes.IdentityCookieOnly;

		// Reads the policy name the endpoint already declares rather than a second marker: one declaration,
		// two consumers, so an endpoint cannot be in the probe lane for authorization and the browser lane
		// for authentication. Must precede the browser fallthrough -- that ordering IS the fix for a probe
		// being handed a cookie.
		foreach (var data in endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
		{
			if (string.Equals(data.Policy, NorsePolicies.Probe, StringComparison.Ordinal))
				return NorseSchemes.Probe;
		}

		return NorseSchemes.Browser;
	}
}
