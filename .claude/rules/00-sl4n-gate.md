# sl4n — gate e invariantes del repo

> Ficha que consumen las skills `/sl-*` (nivel usuario, `~/.claude/skills/`). El **método** vive en
> la skill; los **hechos de este repo** viven acá. Una skill que no encuentra esta ficha se detiene:
> no improvisa un gate.

## Qué es

Puerto .NET de SyntropyLog, sobre `Microsoft.Extensions.Logging`. Publica tres paquetes NuGet:
`sl4n`, `sl4n.AspNetCore`, `sl4n.Testing`. Solución: `sl4n.slnx`.

## Gate (`.github/workflows/ci.yml`)

```
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test tests/sl4n.Tests/sl4n.Tests.csproj --no-build --configuration Release
dotnet publish tests/sl4n.AotSmoke/sl4n.AotSmoke.csproj -c Release -r linux-x64   # smoke AOT
```

❌ NEVER dar por cerrada una tarea con `dotnet build` verde: el smoke AOT es un paso aparte y es el
que atrapa lo que el build no ve.

## Invariantes

- ❌ NEVER romper Native AOT — el `AotSmoke` existe para eso. ✅ ALWAYS evitar reflection y
  serialización en runtime; source-gen o nada.
- ❌ NEVER divergir del modelo de la referencia Node sin registrarlo. ✅ ALWAYS actualizar
  `PARITY-ROADMAP.md`: es el contrato de paridad de este puerto.
- ❌ NEVER que un fallo de logging escape al caller — la promesa "logging never throws" es de la
  familia, no solo del Node.
- ❌ NEVER publicar un paquete sin los tres en la misma versión, si el cambio los cruza.

## Fuente de verdad del estado

`PARITY-ROADMAP.md` → `CHANGELOG.md` → los `.csproj` de `src/`.
