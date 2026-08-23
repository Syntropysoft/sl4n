# sl4n — gate e invariantes del repo

> Ficha que consumen las skills `/sl-*` (nivel usuario, `~/.claude/skills/`). El **método** vive en
> la skill; los **hechos de este repo** viven acá. Una skill que no encuentra esta ficha se detiene:
> no improvisa un gate.

## Cómo se trabaja acá (aplica antes que cualquier otra cosa)

- **La frontera es tuya, el código es mío.** Alcance, límites y fronteras — qué entra y qué no, qué
  se niega a hacer la librería, dónde termina su responsabilidad, qué se borra, qué promete el
  contrato — son decisiones del usuario y **se preguntan**. La implementación, los nombres, la
  estructura, los tests y la verificación son del agente: se hacen, no se consultan.
- **El tell, porque una decisión de frontera casi nunca parece una.** Si la respuesta a *"¿por qué
  así?"* es un **principio**, saliste del código y estás moviendo un límite → preguntá. Si es una
  **técnica**, decidí y seguí.
  *"Uso un Map porque el lookup es O(1)"* → técnica, mío.
  *"Devuelvo `null` porque no adivinamos"* → principio, tuyo.
- ❌ NEVER meter código sin análisis previo. Si algo no queda claro, **se pregunta** — no se elige
  la interpretación más razonable y se sigue.
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
- ❌ NEVER introducir un mecanismo propio para algo que .NET/MEL ya resuelve. ✅ ALWAYS acoplar la
  feature al punto de extensión estándar — DI keyed, `IOptions`/`IOptionsMonitor`, `Activity`,
  `TimeProvider`, scopes de MEL — de modo que se configure **desde donde el usuario ya configura**.
  sl4n aporta el resultado, no el mecanismo: acá no se re-implementa lo del port de JS, se amplifica
  lo que .NET ya trae. Y el acoplamiento **degrada**: si el mecanismo no está presente en tiempo de
  ejecución, hay fallback — nunca una excepción.
- ❌ NEVER mezclar cálculo y efecto cuando se pueden separar. ✅ ALWAYS la **decisión** en una
  función pura y `static` (`Sanitizer.Clean`, `RenderTemplate`, `LevelName`), y el **efecto**
  — mutar el buffer, escribir al sink — en el borde que la llama. Guard clauses primero: el
  fail-path sale arriba, sin `else`. SOLID sobre `ITransport`: se extiende agregando un sink,
  no tocando el worker.
  **El límite, explícito:** el hot path del worker muta buffers reutilizados **a propósito** — una
  asignación por entrada, por millones de entradas — y eso NO se "arregla". La regla no es "nada
  muta": es que lo que se *calcula* salga de funciones puras, y lo que *muta* sea un borde chico,
  nombrado y justificado.
- ❌ NEVER que un fallo de logging escape al caller — la promesa "logging never throws" es de la
  familia, no solo del Node.
- ❌ NEVER publicar un paquete sin los tres en la misma versión, si el cambio los cruza.

## Fuente de verdad del estado

`PARITY-ROADMAP.md` → `CHANGELOG.md` → los `.csproj` de `src/`.
