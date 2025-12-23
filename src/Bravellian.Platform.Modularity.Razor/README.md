# Bravellian.Platform.Modularity.Razor

Razor Pages adapter for engine-first modules.

## Install

```bash
dotnet add package Bravellian.Platform.Modularity.Razor
```

## Usage

```csharp
ModuleRegistry.RegisterModule<MyRazorModule>();

builder.Services.AddModuleServices(builder.Configuration);
builder.Services.AddSingleton<UiEngineAdapter>();

builder.Services.AddRazorPages()
    .ConfigureRazorModulePages();
```

## Examples

### Module with Razor Pages

```csharp
public sealed class MyRazorModule : IModuleDefinition, IRazorModule
{
    public string Key => "my-module";
    public string DisplayName => "My Module";
    public string AreaName => "MyArea";

    public void ConfigureRazorPages(RazorPagesOptions options)
    {
        options.Conventions.AuthorizeAreaFolder(AreaName, "/");
    }

    // IModuleDefinition members omitted for brevity
}
```

### PageModel calling a UI engine

```csharp
public sealed class IndexModel : PageModel
{
    private readonly UiEngineAdapter adapter;

    public IndexModel(UiEngineAdapter adapter)
    {
        this.adapter = adapter;
    }

    public MyViewModel ViewModel { get; private set; } = default!;

    public async Task OnPostAsync(MyCommand command, CancellationToken cancellationToken)
    {
        var response = await adapter.ExecuteAsync<MyCommand, MyViewModel>(
            "my-module",
            "ui.example",
            command,
            cancellationToken);

        ViewModel = response.ViewModel;
    }
}
```

## Documentation

- https://github.com/bravellian/platform
- docs/modularity-quickstart.md
