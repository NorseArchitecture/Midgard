# Norse.Infrastructure.Web.Client

WASM-friendly gRPC client-side failure decoding. `Grpc/RpcExceptionExtensions` decodes an `RpcException`'s `grpc-status-details-bin` trailer using `google.rpc` well-known types and reconstructs a `Problem` object from the `ErrorInfo.Reason`, `Errors`, and `CorrelationId` details — the symmetrical counterpart to `Infrastructure.Web.Server`'s encoding side.

`Authorization/` carries `AddNorseClientAuthorization()`, which a WASM host calls in place of `AddAuthorizationCore()`: it registers the client lane and audience requirement handlers, which treat the browser's principal as the identity lane, so the client mirror of the platform's policies evaluates as the server's does for the cookie identity.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
