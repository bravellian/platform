# Bravellian.Platform.Email.Postmark

Postmark adapter for Bravellian.Platform.Email outbox dispatching.

## Registration

```csharp
services.AddHttpClient<PostmarkEmailSender>();
services.AddSingleton<IOutboundEmailSender>(sp =>
{
    var client = sp.GetRequiredService<HttpClient>();
    return new PostmarkEmailSender(client, new PostmarkOptions
    {
        ServerToken = "server-token",
        MessageStream = "outbound"
    });
});
```

See `/docs/email/README.md` for architecture and quick start examples.
