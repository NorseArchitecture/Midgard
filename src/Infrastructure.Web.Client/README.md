# Norse.Infrastructure.Web.Client

WASM-friendly gRPC client-side failure decoding. `Grpc/RpcExceptionExtensions` decodes an `RpcException`'s `grpc-status-details-bin` trailer using `google.rpc` well-known types and reconstructs a `Problem` object from the `ErrorInfo.Reason`, `Errors`, and `CorrelationId` details — the symmetrical counterpart to `Infrastructure.Web.Server`'s encoding side.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
