using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>Composition of the platform's standard mediator pipeline — the one composition site.</summary>
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		/// Registers the standard behavior chain — registration order <b>is</b> chain order, and it is
		/// law (spec §2.2): Telemetry → ExceptionTranslation → Authorization → Validation → handler —
		/// plus the scoped <see cref="PrincipalAccessor"/>, the dispatch map, and the
		/// <see cref="ISender"/>. A product realm appends its own <c>IBehavior&lt;,&gt;</c> registration
		/// after this call; it lands between Validation and the handler. Idempotent: a second call
		/// no-ops rather than double-running the chain.
		/// </summary>
		public IServiceCollection AddNorsePipeline()
		{
			if (services.Any(descriptor => descriptor.ServiceType == typeof(ISender)))
				return services;

			services.AddScoped(typeof(IBehavior<,>), typeof(TelemetryBehavior<,>));
			services.AddScoped(typeof(IBehavior<,>), typeof(ExceptionTranslationBehavior<,>));
			services.AddScoped(typeof(IBehavior<,>), typeof(AuthorizationBehavior<,>));
			services.AddScoped(typeof(IBehavior<,>), typeof(ValidationBehavior<,>));
			services
				.AddScoped<PrincipalAccessor>()
				.AddScoped<IPrincipalAccessor>(provider => provider.GetRequiredService<PrincipalAccessor>())
				.AddSingleton<SenderDispatchMap>()
				.AddScoped<ISender, Sender>();
			return services;
		}
	}
}
