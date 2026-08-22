# sl4n — gate e invariantes del repo

> Ficha que consumen las skills `/sl-*` (nivel usuario, `~/.claude/skills/`). El **método** vive en
> la skill; los **hechos de este repo** viven acá. Una skill que no encuentra esta ficha se detiene:
> no improvisa un gate.

## Cómo se trabaja acá (aplica antes que cualquier otra cosa)

- ❌ NEVER meter código sin análisis previo. Si algo no queda claro, **se pregunta** — no se elige
  la interpretación más razonable y se sigue.
- **La línea.** Se decide solo: nombres, ubicación de archivos, forma del test, redacción. Se
  **pregunta siempre** ante: (a) cambio de comportamiento observable para alguien que ya usa esto,
  (b) borrar o reemplazar contenido existente, (c) elegir entre dos semánticas defendibles,
  (d) mover superficie pública.
- ✅ ALWAYS **el contexto que aporta el usuario se convierte en test**, no en doc. Un hecho de
  comportamiento — *"esto ya está en producción"*, *"siempre funcionó así"*, *"no cambies esto"* —
  se fija con un test cuyo **nombre** lo enuncia. Un doc se lee si alguien lo busca; un test falla
  cuando alguien lo contradice, sin que nadie recuerde la conversación.
- ❌ NEVER afirmar una negación ("no existe X") sin decir **dónde** se buscó.
- ❌ NEVER borrar porque el archivo destino existe. ✅ ALWAYS comparar **contenido**.
- ❌ NEVER una sonda manual como evidencia: o se vuelve test, o se dice "verificado a mano, sin test".

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
