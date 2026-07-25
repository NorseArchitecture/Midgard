# Norse.Infrastructure.Web.Server

gRPC server-side failure encoding (`Mediator/Grpc`) — translates `Problem` into `google.rpc.Status` + `ErrorInfo`/`BadRequest`/`DebugInfo` well-known types on a `grpc-status-details-bin` trailer, paired with `Infrastructure.Web.Client` for symmetrical decoding. Also carries the four-stage mediator pipeline (`AuthorizationBehavior`, `ExceptionTranslationBehavior`, `TelemetryBehavior`, `ValidationBehavior`, all `internal sealed`) and the `IDeferredSignIn` implementation (`DeferredSignIn/`) against Asgard's `Abstractions.Web.Server` contract.

The four `Behavior` classes are constructed directly by Asgard's `GatewayGenerator` inside `InProcessHost`-mode consumers — cross-assembly access needs a per-consumer `InternalsVisibleTo` grant here, not a `public` widening.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
