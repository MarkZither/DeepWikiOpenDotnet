# VectorStoreFactory Refactor Proposal 🔧✨

## Summary

**Goal:** Replace brittle, magic-string and runtime-type-based provider selection with a typed, explicit, extensible provider registry pattern. This improves safety, testability, and extensibility (e.g., Pinecone, SQLite, etc.).

---

## Problem Statement ⚠️

Current implementation (in `VectorStoreFactory`) has several issues:

- Uses magic configuration keys and literal provider name strings (e.g., `"sqlserver"`, `"postgres"`).
- Uses `Type.GetType("Full.Type.Name, Assembly")` to load provider adapters at runtime — fragile and may be insecure (DLL hijacking).
- Resolves `IVectorStore` indirectly in a way that caused recursive DI resolution and infinite logging loops during startup.
- Not easily extensible: adding new providers requires modifying factory code and adding magic strings in multiple places.
- Hard to test and reason about; provider-specific checks and construction are spread and ad-hoc.

---

## Design Principles ✅

- Explicit over implicit: prefer typed configuration and DI registration rather than strings and reflection.
- Single responsibility: factors provider discovery, availability checks, and construction into provider-specific types.
- Extensible: adding new providers should be as simple as implementing a provider class and registering it with DI.
- Secure: avoid loading arbitrary assemblies or types from configuration.
- Testable: provider selection and fallback behavior should be unit-testable without loading provider assemblies.

---

## Proposed API & Types

- `IVectorStoreProvider`
  - Responsibilities:
    - `string Name { get; }` — canonical name for the provider (e.g., `"postgres"`, `"sqlserver"`, `"pinecone"`).
    - `bool IsAvailable(IConfiguration config)` — provider-specific availability checks.
    - `IVectorStore Create(IServiceProvider sp)` — construct provider adapter using DI-scoped services.

- `VectorStoreOptions` bound from config (`VectorStore` section) containing `Provider` and provider-specific settings sections.

- `VectorStoreFactory` injected with `IEnumerable<IVectorStoreProvider>`, `IOptions<VectorStoreOptions>`, `IConfiguration`, and `ILoggerFactory`.
  - Select provider by name, call `IsAvailable`, then `Create`.
  - Fallback behavior: explicit (either `NoOpVectorStore` or throw depending on `VectorStore:AllowNoOpFallback`).

- `AddVectorStoreProvider<TProvider>(IServiceCollection)` extension helper to register providers.

---

## Example Interfaces & Snippets

```csharp
// IVectorStoreProvider
public interface IVectorStoreProvider
{
    string Name { get; }
    bool IsAvailable(IConfiguration configuration);
    IVectorStore Create(IServiceProvider serviceProvider);
}

// VectorStoreOptions
public class VectorStoreOptions
{
    public const string Section = "VectorStore";
    public string Provider { get; set; } = "sqlserver";
    public SqlServerOptions? SqlServer { get; set; }
    public PostgresOptions? Postgres { get; set; }
}
```

Provider example (Postgres):

```csharp
public class PostgresVectorStoreProvider : IVectorStoreProvider
{
    public string Name => "postgres";

    public bool IsAvailable(IConfiguration configuration)
    {
        var conn = configuration.GetSection(VectorStoreOptions.Section)["Postgres:ConnectionString"];
        return !string.IsNullOrEmpty(conn) || !string.IsNullOrEmpty(configuration.GetConnectionString("Postgres")) || CheckCommonNames(configuration);
    }

    public IVectorStore Create(IServiceProvider sp)
    {
        // Resolve Postgres persistence and adapter explicitly (registered in DI)
        var adapter = sp.GetService<DeepWiki.Data.Postgres.VectorStore.PostgresVectorStoreAdapter>();
        return adapter ?? new NoOpVectorStore();
    }
}
```

Refactored factory sketch:

```csharp
public class VectorStoreFactory
{
    private readonly IEnumerable<IVectorStoreProvider> _providers;
    private readonly IOptions<VectorStoreOptions> _options;
    private readonly ILogger<VectorStoreFactory> _logger;

    public VectorStoreFactory(IEnumerable<IVectorStoreProvider> providers, IOptions<VectorStoreOptions> options, ILogger<VectorStoreFactory> logger) { ... }

    public IVectorStore Create(IServiceProvider sp)
    {
        var providerName = _options.Value.Provider ?? "sqlserver";
        var provider = _providers.FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase) && p.IsAvailable(_configuration));
        if (provider == null) return new NoOpVectorStore();
        return provider.Create(sp);
    }
}
```

---

## Migration Plan (low-risk)

1. Add `IVectorStoreProvider` and `VectorStoreOptions` types and tests.
2. Implement existing `SqlServer` and `Postgres` providers.
3. Register providers in `Program.cs` (e.g., `services.AddSingleton<IVectorStoreProvider, PostgresVectorStoreProvider>();`).
4. Update `VectorStoreFactory` to consume `IEnumerable<IVectorStoreProvider>` and `IOptions<VectorStoreOptions>`.
5. Add unit tests for provider selection and fallback behavior.
6. Keep current behaviour intact by default (NoOp fallback) and use feature toggle `VectorStore:AllowNoOpFallback` if desired.
7. Optional: add a health check and an admin diagnostic endpoint listing selected provider and availability checks.

> Note: Implement steps in small PRs with tests for each change to keep the changeset reviewable and safe.

---

## Security & Robustness Notes 🛡️

- Avoid `Type.GetType("...")` and reflection-based loading of provider assemblies driven by user config.
- Prefer explicit DI registration of providers so assembly loading is controlled by the application, not runtime strings.
- Validate provider-specific config at startup and fail fast (unless `AllowNoOpFallback=true` is explicitly configured).

---

## Tests & Acceptance Criteria ✅

- Unit tests for `VectorStoreFactory`:
  - Correct provider selected when present and available.
  - `NoOpVectorStore` returned when provider not available and fallback allowed.
  - Throw or fail startup when provider not available and fallback disabled.
- Integration test: register Postgres provider and confirm `Create()` returns `PostgresVectorStoreAdapter` when `ConnectionString` is present.

---

## Follow-up Improvements

- Add a provider discovery mechanism for plugin scenarios (explicit plugin registry, not untrusted config-driven assembly loads).
- Add lightweight telemetry events for provider selection and health.
- Add an automated policy check to the CI to ensure new providers are registered with the factory tests.

---

If you'd like, I can create an initial PR with the refactor scaffold (interfaces, options, provider for Postgres, updated factory, and tests) and keep the current behavior behind a safe toggle so it’s backwards-compatible. Want me to draft that PR? 

---

*File generated for reference and future refactor planning.*
