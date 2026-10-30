using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Web.Server.Authorization;

/// <summary>
///     The one failure reason the mediator's authorization behavior maps to the re-authentication hop: a principal
///     whose authentication time is missing, unparseable, in the future beyond the skew allowance, or older than
///     the window. Every other failure reason stays a bare <see cref="ErrorCategory.Forbidden" />.
/// </summary>
/// <param name="handler">The handler that failed the requirement.</param>
sealed class ReauthenticationRequiredReason(IAuthorizationHandler handler) :
	AuthorizationFailureReason(handler, NorseErrorCodes.ReauthenticationRequired);
