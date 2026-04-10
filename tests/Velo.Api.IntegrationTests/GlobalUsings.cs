global using Xunit;

// Integration tests share HostFactoryResolver static state — running factories
// in parallel causes "entry point exited without ever building an IHost" errors.
// Sequential execution ensures each WebApplicationFactory builds cleanly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
