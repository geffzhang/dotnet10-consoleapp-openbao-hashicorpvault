# Aspire orchestration for OpenBao

## Goal and scope

Add an Aspire AppHost that orchestrates OpenBao and the existing .NET 10
VaultSharp console application. Follow the container, endpoint injection, and
dependency patterns in the supplied BellaBaxter.Aspire.Host reference without
depending on the Bella Baxter stack.

OpenBao is pinned to `ghcr.io/openbao/openbao:2.7.2` in both modes.
The user approved two modes: PostgreSQL-backed persistent storage by default,
and an explicitly selected in-memory development server.

This is a local learning and demonstration setup, not a production deployment.
Existing Docker Compose behavior and existing Raft data remain unchanged.

## Approach

Use one AppHost project with two launch profiles and small, local hosting
extensions only where needed. Do not introduce a separate integration library.
Add the AppHost to the existing solution.

Direct Aspire container orchestration is preferred over wrapping Compose because
the AppHost must own resource dependencies and inject resolved endpoints.
A reusable hosting package would add unnecessary project and API overhead here.

## Persistent profile

- Start PostgreSQL with a named data volume and a dedicated OpenBao database.
- Start OpenBao only after the database is ready.
- Configure OpenBao's `postgresql` storage backend. Allow OpenBao to create its
  storage tables; do not introduce a separate schema migration service.
- Supply database credentials through secret parameters and runtime environment
  variables, not through checked-in connection strings or configuration files.
  Use the PostgreSQL environment variables supported by OpenBao's backend.
- Disable database TLS only for this local container network. Document that
  production deployments need TLS and appropriate database permissions.
- Enable the OpenBao UI and an HTTP API endpoint. Resolve the host-facing
  endpoint through Aspire rather than assuming a fixed port in the console.
- Keep server initialization and unsealing manual. Do not capture or store
  unseal keys or root tokens in project files or automatic initialization logs.
- Treat uninitialized and sealed states as not ready. Start the console only
  after OpenBao's readiness check succeeds.
- Never seed or overwrite persistent secrets automatically. Document explicit
  KV v2 enablement and the existing two demonstration secret paths.
- Store secret data in PostgreSQL's volume. An OpenBao restart requires
  unsealing, but must preserve secrets and their versions.

On the first run, start infrastructure without requiring a usable console token.
The console must be startable explicitly after initialization, unsealing, KV v2
setup, and token configuration. Document any necessary AppHost restart to load
new environment variables or user secrets. Missing credentials must not prevent
the user from starting the infrastructure needed to obtain them.

## Development profile

- Run OpenBao with `server -dev`; PostgreSQL is not needed in this profile.
- Supply a development root token through an Aspire secret parameter rather
  than a source-code literal. The same parameter authenticates the seed
  operation and the console.
- Wait for OpenBao readiness before writing the existing demonstration secrets:
  `secret/app/config` and `secret/integracoes/api`.
- Reuse the current demonstration fields and values. Clearly label them as
  public demonstration data, never real credentials.
- Start the console only after successful seeding. A failed seed operation
  must be visible and must not be reported as success.
- Document that secrets disappear on restart and that the development server
  is unsuitable for production.

## Console integration and credentials

Keep the current Token authentication, KV v2 paths, Chinese messages, and
standalone execution behavior.

- Continue reading the token from `BAO_TOKEN`.
- Read the server URI from `BAO_ADDR` when supplied by Aspire.
- Use `http://localhost:1337` only when `BAO_ADDR` is absent, preserving the
  existing standalone default.
- Reject explicitly configured empty or invalid addresses with a clear error.
- Inject the resolved OpenBao endpoint and token into the console through
  Aspire environment configuration.
- Support explicit console startup in persistent mode; automated startup in
  development mode must not depend on interactive terminal input.
- Preserve the standalone press-Enter exit behavior and avoid waiting for input
  when the console is launched noninteractively.

Use Aspire secret parameters and user secrets for sensitive inputs. Never commit
tokens, database passwords, unseal keys, or local credential files. Do not add
custom logging of credentials. Document the existing console's intentional
plaintext output of demonstration secrets and the development server's unsafe
logging characteristics.

## Compatibility and operational boundaries

- Keep the existing Compose file as an independent Raft-backed workflow.
- Do not share its data volume or automatically migrate its data into PostgreSQL.
- Explain the difference between selecting a profile and migrating stored data.
- Preserve the current console project and its direct `dotnet run` workflow.
- Keep all API and database exposure local to the development machine; do not
  publish externally accessible endpoints.
- Do not add HA, automatic unsealing, production TLS, backup automation, or
  unrelated Bella Baxter services.

## Documentation

Update the root README with prerequisites, solution/AppHost commands, both
profiles, parameter setup, resolved endpoint discovery, manual initialization
and unsealing, explicit demonstration seeding, console startup, and persistence
semantics. Explain that ordinary resource shutdown must not delete database
volumes and that removing volumes destroys stored data.

## Verification and acceptance

1. Build the AppHost and existing console through the solution.
2. Verify both orchestration models: pinned image, profile-specific resources,
   named PostgreSQL volume, secret parameter references, endpoint injection,
   readiness dependencies, and seed completion dependencies.
3. Verify `BAO_ADDR` default, configured address, invalid address, and missing
   token behavior without contacting a real secrets service where possible.
4. With Docker available, run development mode and verify both secret paths and
   console completion.
5. With Docker available, initialize and unseal a disposable PostgreSQL-backed
   instance, enable KV v2, write and read test secrets, then restart OpenBao and
   verify that data remains after unsealing.
6. Confirm persistent startup never seeds or overwrites secrets automatically.
7. Leave existing Compose resources and user data untouched. Clean up only
   resources created for these checks.

If Docker or another required runtime is unavailable, report the blocked runtime
checks explicitly; a successful build alone does not establish persistence.

## References

- Supplied local reference: `BellaBaxter.Aspire.Host` in the
  `bella-baxter-dotnet` repository.
- [OpenBao PostgreSQL storage](https://openbao.org/docs/configuration/storage/postgresql/):
  backend environment variables and automatic table creation.
- [OpenBao development server](https://openbao.org/docs/concepts/dev-server/):
  automatic initialization/unsealing and nonpersistent in-memory storage.
