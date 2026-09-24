# Security and limitations

Only multipart CSV uploads are accepted and size is capped at 1 MiB. The service never accepts a filesystem path or executes a shell command. API failures return generic messages, and source content is synthetic only. SQLite is local persistence, not encryption, authorization, retention policy, accounting advice, or production compliance. No credentials, external writes, or FX processing are included.

Browser E2E remains configured for public and CI use. A local Remote Desktop Commander validation environment may isolate .NET loopback traffic; that environment limitation is not a product runtime claim.
