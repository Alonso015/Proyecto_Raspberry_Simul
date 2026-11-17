# Hoja de ruta: prompts para terminar el IMAT Pi Simulator

Secuencia de prompts para completar el proyecto. **Cada prompt es una etapa pequeña que termina en algo que puedes probar.** No pases al siguiente hasta haber comprobado el anterior y entender lo que se ha hecho.

Base de todas las etapas: [requisitos.md](requisitos.md), [arquitectura.md](arquitectura.md) y [hardware.md](hardware.md).

## Cómo usar esta hoja

1. Copia el prompt de la etapa y pégalo en Claude Code, en esta carpeta.
2. Cuando termine, haz las comprobaciones de **"Comprueba"** tú mismo (ejecutar tests, abrir Unity, jugar).
3. Repasa **"Deberías entender"**. Si algo no lo entiendes, pregúntalo antes de seguir.
4. Si todo está bien, pide el commit: *"Confirma los cambios del árbol de trabajo con un mensaje adecuado."*
5. Marca la etapa como hecha en la tabla del final.

### Reglas generales

Todos los prompts terminan con *"Sigue las reglas de docs/hoja_de_ruta.md"*, que equivale a lo siguiente:

- Trabaja en una **rama nueva** con el nombre que indica la etapa, creada a partir de `main`.
- **Implementa solo lo que pide la etapa.** No adelantes funcionalidad de etapas posteriores.
- En el dominio, **escribe los tests** (NUnit) junto con el código, y ejecútalos antes de terminar.
- Al terminar, explica:
  - qué has hecho, por capas: código → pin → circuito → lo que se observa, cuando aplique;
  - qué patrones has usado y por qué;
  - **qué comandos o pasos** tengo que hacer para comprobarlo.
- Si una decisión de diseño no está tomada en `docs/`, **pregúntame** antes de elegir.
- Registra en `docs/` las decisiones nuevas y las aproximaciones de la simulación.
- **No hagas commit** hasta que yo lo pida.

### Calendario orientativo

| Bloque | Etapas | Hasta |
|--------|--------|-------|
| 0. Preparación | E0.1–E0.2 | 19/11/2026 |
| 1. Modelo eléctrico | E1–E5 | 28/11/2026 |
| **Entrega 1** (descripción + UML) | E-UML | **03/12/2026** (prepararla el 30/11) |
| 2. Intérprete | E6–E9 | 12/12/2026 |
| 3. Motor y retos | E10–E11 | 16/12/2026 |
| 4. Protoboard y A\* | E12–E13 | 20/12/2026 |
| 5. Unity | E14–E18 | 03/01/2027 |
| 6. Cierre y **Entrega 2** | E19–E21 | **10/01/2027** (cerrar el 07/01) |

**Importante:** la etapa **E-UML se hace en su fecha** (antes del 03/12), aunque no hayas terminado el bloque 1. Es una entrega evaluada.

**Si trabajas en pareja:** los bloques 2 (intérprete) y 4 (protoboard y A\*) dependen solo del bloque 1. Pueden avanzar en paralelo, cada uno en su rama, una persona en cada uno.

---

## Bloque 0. Preparación

### E0.1. Ordenar el repositorio y subirlo a GitHub

```text
Antes de empezar con el código quiero dejar el repositorio en orden:
1. Haz commit de lo pendiente de la rama docs/arquitectura (arquitectura.md, uml/, hoja_de_ruta.md y los cambios en hardware.md, requisitos.md y .gitignore), en commits separados por tema.
2. Crea la rama main a partir de esa rama, de forma que contenga todo el historial actual.
3. Antes de subir nada, enséñame qué se va a subir y comprueba que los PDF de la asignatura y CLAUDE.local.md quedan fuera.
4. Cuando lo confirme, sube main a origin y deja main como rama de referencia.
Sigue las reglas de docs/hoja_de_ruta.md (en esta etapa sí puedes hacer commit y push cuando yo lo confirme).
```

**Comprueba:**

- En GitHub (`Alonso015/Proyecto_Raspberry_Simul`) aparece `main` con `docs/`.
- No aparecen los PDF ni `CLAUDE.local.md`.

**Deberías entender:**

- Para qué sirven las ramas y por qué cada etapa va en una.
- Qué hace `.gitignore`.

### E0.2. Proyecto Unity y estructura de código

> Antes de este prompt: **instala Unity 6000.6.8f1** desde Unity Hub. Si no la encuentras, búscala en el archivo de versiones de la web de Unity.

```text
Ya tengo instalado Unity 6000.6.8f1. Crea la estructura del proyecto según docs/arquitectura.md §1 (dominio en C# puro, independiente de Unity):
- El repo es el proyecto Unity (Assets/, Packages/, ProjectSettings/). Crea el proyecto 2D con esa versión y añade el .gitignore estándar de Unity sin perder las reglas actuales.
- Assets/Scripts/Domain con un asmdef sin referencias al motor (noEngineReferences).
- Assets/Tests/EditMode con un asmdef de tests NUnit que referencie el dominio.
- tools/DomainTests: un proyecto dotnet de tests que compile esas mismas carpetas, para ejecutar los tests sin abrir Unity. El dominio debe compilar con C# 9 y .NET Standard 2.1, lo que admite Unity 6.
- Un test trivial que pase en los dos sitios.
Explícame qué es un asmdef y por qué el dominio no puede depender de UnityEngine.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: chore/estructura-unity.
```

**Comprueba:**

- `dotnet test tools/DomainTests` en verde.
- En Unity, *Window → General → Test Runner → EditMode*: el mismo test en verde.
- El proyecto abre sin errores en la consola.

**Deberías entender:**

- La separación entre dominio y presentación.
- Qué son los asmdef.
- Por qué los tests se pueden ejecutar fuera de Unity.

---

## Bloque 1. Modelo eléctrico (dominio)

### E1. Solver nodal con resistencias

```text
Etapa 1 del modelo eléctrico (docs/arquitectura.md §6.1, diagrama página 1): implementa Node, Terminal, Netlist, NetlistBuilder (union-find), IElectricalElement, IStampContext, ISolution, MatrixStamper (eliminación gaussiana con pivoteo parcial) y NodalSolver, con un único elemento: Resistor. Los raíles 3,3 V y GND son nodos de tensión fija. Incluye gmin para los nodos flotantes.
Tests:
1. Divisor 3,3 V – 1 kΩ – 1 kΩ – GND → 1,65 V.
2. Divisor 270 Ω / 1 kΩ con el valor calculado a mano.
3. Corriente por una resistencia = V/R.
4. Un nodo sin conectar da 0 V y no rompe el solver.
5. Unir 3,3 V y GND en el mismo nodo se detecta como cortocircuito al construir la netlist.
Explícame con un ejemplo numérico cómo queda la matriz G·V = I del divisor y cómo la "estampa" cada resistencia.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/solver-nodal.
```

**Comprueba:**

- Los 5 tests en verde.
- Haz tú a mano el divisor 270 Ω / 1 kΩ y compara con el resultado.

**Deberías entender:**

- El análisis nodal.
- Por qué cada resistencia suma conductancias en 4 posiciones de la matriz.
- Para qué sirve `gmin`.
- Cómo union-find agrupa los terminales en nodos.

### E2. GPIO y pulsador

```text
Etapa 2 del modelo eléctrico: añade GpioPin (docs/arquitectura.md §6.1 y diagrama página 3) como elemento eléctrico:
- salida = equivalente de Norton con resistencia interna (supuesto documentado);
- entrada = alta impedancia con pull-up o pull-down interna opcional;
- lectura de nivel con un umbral (supuesto documentado).
Añade también PushButton (interruptor).
Tests:
1. Pulsador entre GPIO y GND con pull-up: en reposo se lee HIGH; pulsado, LOW.
2. Sin pull-up y sin pulsar, el nodo queda flotante; explícame qué lee el simulador y qué pasaría en la realidad.
3. GPIO como salida HIGH con una resistencia de 1 kΩ a GND: corriente ≈ 3,3 mA.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/gpio-pulsador.
```

**Comprueba:** los tests en verde. Cambia la pull-up por pull-down en el test 1 y razona qué tendría que leerse antes de ejecutarlo.

**Deberías entender:**

- Qué es una pull-up y por qué en la IMAT HAT el pulsador es activo a nivel bajo.
- El equivalente de Norton del GPIO.

### E3. LED y detección de fallos

```text
Etapa 3 del modelo eléctrico: implementa Led como elemento no lineal (INonlinearElement, lineal a trozos: no conduce por debajo de la tensión umbral; por encima, umbral + resistencia pequeña) y la iteración del solver hasta que los diodos sean coherentes (máximo de iteraciones). Implementa IFaultSource, Fault, FaultKind y FaultDetector con estas reglas de docs/requisitos.md §2.6:
- LED por encima de su corriente máxima;
- GPIO por encima de 7 mA;
- cortocircuito;
- más de 3,3 V en un GPIO.
Tests:
1. GPIO HIGH → 270 Ω → LED → GND: ≈ 4,8 mA, encendido y sin fallos (es el circuito real de la HAT).
2. El mismo sin resistencia → fallo de LED y de GPIO.
3. LED al revés → no conduce.
4. GPIO en salida HIGH unido a GND → fallo de GPIO.
5. Un divisor alimentado a 5 V hacia un GPIO → sobretensión.
Explícame con números por qué el test 2 falla y qué pasaría en la placa real.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/led-fallos.
```

**Comprueba:** los tests en verde. Cambia 270 Ω por 100 Ω y predice si salta algún fallo antes de ejecutar.

**Deberías entender:**

- El modelo lineal a trozos del diodo.
- Por qué hay que iterar.
- Cómo salen los fallos de las corrientes calculadas, sin reglas escritas a mano.

### E4. Componentes y la IMAT HAT completa

```text
Etapa 4: capa de Componentes (docs/arquitectura.md §6.2, diagrama páginas 2 y 3):
- Component, Pin, ComponentType (Flyweight), IComponentFactory/ComponentFactory (Factory) y CompositeComponent (Composite).
- Potentiometer y Ldr (con la curva de la práctica 5, docs/hardware.md §4).
- RaspberryPi (GPIO BCM 0–27, raíles).
- ImatHat como compuesto, cableada EXACTAMENTE según docs/hardware.md §3: D1–D8 con R1–R8 de 270 Ω y JP1/JP2; SW1–SW4; RV1 a CH7 por JP3; los 4 conectores de servo como pines.
- Mcp3008 todavía como entradas de alta impedancia, sin conversión.
Tests de integración sobre la HAT:
1. GPIO20 HIGH → D1 encendido con ≈ 4,8 mA; el resto apagados.
2. SW1 pulsado → GPIO7 lee LOW.
3. RV1 al 50 % → 1,65 V en el nodo de CH7.
4. JP1 cortado → D1–D4 no se encienden aunque el GPIO esté a HIGH.
Explícame dónde aparece cada patrón y qué problema resuelve.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/componentes-hat.
```

**Comprueba:**

- Los tests en verde.
- Revisa el código de `ImatHat` con el esquema delante y comprueba que cada conexión coincide.

**Deberías entender:**

- Composite: la HAT se trata como una resistencia suelta.
- Flyweight: qué datos se comparten y cuáles no.
- Cómo crea la Factory los componentes.

### E5. PWM, servo, ADC y conflictos de pines

```text
Etapa 5: comportamiento y PWM (docs/arquitectura.md §6.4, docs/requisitos.md §2.6):
- PwmSignal y modo PWM en GpioPin, con la propuesta de §6.4: para los fallos se resuelve en HIGH; el LED escala su brillo por el factor de servicio; el servo lee el ancho de pulso.
- IBehavioralComponent.
- Servo (0,5–2,5 ms → 0–180°, velocidad máxima como supuesto).
- Mcp3008 (10 bits sobre 3,3 V).
- Conflictos de pines de docs/hardware.md §3.9 (SPI activo y SW1/GPIO7, etc.) como fallos PinConflict.
Antes de implementar, pregúntame si cerramos la decisión de §6.4 tal como está.
Tests:
1. PWM al 25 % en GPIO20 → brillo de D1 al 25 % del máximo.
2. Servo con pulso de 1,5 ms → objetivo de 90° y el ángulo se acerca con el tiempo.
3. RV1 al 50 % → lectura de CH7 ≈ 512.
4. SPI activo y SW1 en uso → conflicto.
Explícame la diferencia entre "potencia media" y "pulso de control" con estos dos casos.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/pwm-servo-adc.
```

**Comprueba:** los tests en verde. Calcula a mano qué lectura del ADC daría 1,0 V.

**Deberías entender:**

- Las dos interpretaciones del PWM.
- La cuantización de 10 bits.
- Por qué el pulsador de GPIO7 choca con SPI.

---

## Entrega 1 (hacer antes del 03/12/2026)

### E-UML. Descripción y UML de la arquitectura

```text
Preparamos la entrega 1 del 03/12/2026 (docs/requisitos.md §1.3): descripción del proyecto (objetivo, funcionamiento y esquema general) + diseño UML de la arquitectura detallando los patrones.
1. Actualiza docs/uml/01_modelo_electrico_componentes.drawio para que coincida con el código ya implementado. Si he retocado el .drawio a mano, respeta mis cambios.
2. Crea los diagramas que faltan, todavía como diseño: intérprete y API gpiozero (Interpreter, Composite, Visitor, Bridge), motor de simulación (Facade), retos y estados del juego (State), protoboard y A* (Strategy). Antes de dibujar el intérprete, decidamos cómo se pausa el programa (docs/arquitectura.md §7).
3. Mismo estilo que Practica_4.drawio, sin líneas que crucen clases ni que lleguen al mismo punto.
4. Redacta la descripción del proyecto como documento para entregar.
5. Exporta los diagramas a PDF o PNG y prepara el .zip 1_NombreApellido1_NombreApellido2_IMATPiSimulator_26_27 (pregúntame los nombres).
Sigue las reglas de docs/hoja_de_ruta.md. Rama: docs/entrega-1.
```

**Comprueba:**

- Abre cada diagrama en draw.io.
- Lee la descripción como si fueras el profesor.
- Revisa el `.zip` antes de entregarlo.

**Deberías entender:** poder explicar cada patrón del UML y el problema que resuelve. Te lo pueden preguntar.

---

## Bloque 2. Intérprete del subconjunto de Python

### E6. Analizador léxico

```text
Etapa 6: lexer del subconjunto de Python (docs/requisitos.md §2.5). Tokens: identificadores, números, cadenas, operadores (incluidos los de bits), palabras clave, y sangría como INDENT/DEDENT. Errores con número de línea y columna.
Tests con fragmentos reales de las prácticas, por ejemplo el bucle de scan con if/else anidado.
Explícame cómo se convierte la sangría de Python en tokens INDENT/DEDENT.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/lexer.
```

**Comprueba:** los tests en verde. Prueba un programa mal sangrado y mira el mensaje de error.

**Deberías entender:** qué es un token y por qué la sangría es parte de la gramática de Python.

### E7. Analizador sintáctico y AST

```text
Etapa 7: parser de descenso recursivo que construya el AST (patrón Composite: Expr y Stmt). Precedencia de operadores correcta. Errores sintácticos legibles con línea.
Añade un Visitor que imprima el AST, para poder verlo.
Tests:
- precedencia (2 + 3 * 4, desplazamientos de bits);
- if/elif/else, while, for range, def/return, lambda simple, llamadas encadenadas (button.when_pressed = ...).
Explícame la gramática que has implementado y dónde están Composite y Visitor.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/parser-ast.
```

**Comprueba:** imprime el AST de un programa de la práctica 3 y compruébalo a mano.

**Deberías entender:**

- Cómo el descenso recursivo refleja la precedencia.
- Composite en el AST.
- Visitor.

### E8. Ejecución cooperativa

```text
Etapa 8: ejecución del AST.
1. Primero decidamos cómo se pausa y reanuda el programa (docs/arquitectura.md §7): evaluador con iteradores de C# o bytecode. Compara las dos opciones con ejemplos de este proyecto y recomiéndame una. Espera a que decida.
2. Implementa: variables y ámbitos, funciones, if/while/for, print, sleep en tiempo simulado, y un presupuesto de instrucciones por tick (un while True sin sleep no puede colgar el juego). Errores de ejecución con línea.
Tests: con un reloj simulado, un programa con sleep(0.5) en bucle avanza lo esperado tras N ticks; un bucle infinito no bloquea.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/ejecucion.
```

**Comprueba:** los tests en verde. Ejecuta un programa con `print` y `sleep` paso a paso y comprueba los tiempos.

**Deberías entender:**

- Qué significa "cooperativo".
- Por qué no podemos usar hilos ni el `sleep` real.

### E9. API gpiozero simulada (Bridge)

```text
Etapa 9: API gpiozero simulada sobre los GPIO del modelo, con el patrón Bridge (abstracción: clases de la API; implementación: IPinFactory/IPin sobre GpioPin), como la pin_factory real de gpiozero.
Clases: LED, PWMLED, LEDBoard, Button (is_pressed, value, when_pressed, when_released, wait_for_press; callbacks con lambda), AngularServo, MCP3008.
Imports permitidos: from gpiozero import ..., from time import sleep. Cualquier otro import da un error claro.
Tests de integración: ejecutar contra la HAT simulada los programas de ejemplo del Tema 3 y de la práctica 3 (LED mientras pulso, conmutación por flanco, contador binario) y comprobar los LEDs.
Explícame el Bridge y cómo un callback when_pressed llega a ejecutarse.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/api-gpiozero.
```

**Comprueba:** escribe tú un programa pequeño, sin mirar las soluciones, y que pase en un test.

**Deberías entender:**

- Bridge.
- Cómo se conecta el lenguaje con el hardware simulado.

---

## Bloque 3. Motor de simulación y retos

### E10. Motor de simulación (Facade)

```text
Etapa 10: SimulationEngine con reloj de tiempo simulado y el tick de docs/arquitectura.md §3 (intérprete → pines → solver → comportamientos → fallos → eventos).
Las entradas se leen con la tensión del tick anterior. Solo se resuelve el circuito si algo ha cambiado.
SimulatorFacade como punto de entrada único para la UI.
Eventos (Observer) para cambios de estado y fallos.
Tests: el programa "LED mientras pulso" con una secuencia de pulsaciones simuladas durante 2 s; un fallo detiene la simulación y emite el evento.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/motor-simulacion.
```

**Comprueba:**

- Los tests en verde.
- Un pequeño programa de consola (en `tools/`) que ejecute una simulación y muestre el estado de los LEDs por pantalla.

**Deberías entender:**

- El orden del tick.
- Facade.
- Observer.

### E11. Retos, verificador y estados del juego

```text
Etapa 11: retos en el dominio (docs/requisitos.md §2.2 y §2.8).
- Definición de reto (componentes disponibles, objetivo, límites), verificador (inyecta estímulos y comprueba salidas) y condiciones de victoria y derrota.
- Máquina de estados del juego (patrón State): menú, selección, montaje, ejecución, victoria, derrota. Reinicio limpio.
- Datos de los retos 1, 2, 3, 6, 7 y 8 (los que no necesitan protoboard). Antes, pregúntame si los guardamos como ScriptableObject o JSON (docs/arquitectura.md §7).
Tests: para cada reto, una solución de referencia gana y al menos una solución errónea pierde por el motivo esperado.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/retos.
```

**Comprueba:** los tests en verde. Escribe tú una solución distinta a la de referencia para el reto 3 y comprueba que gana.

**Deberías entender:**

- Cómo verificar un comportamiento temporal.
- El patrón State y por qué garantiza el reinicio limpio (requisito R4).

---

## Bloque 4. Protoboard y A\*

### E12. Modelo de protoboard

```text
Etapa 12: modelo de la protoboard en el dominio: rejilla de agujeros, tiras conectadas internamente (columnas de 5 y raíles de alimentación), colocación de componentes por sus pines, cables entre agujeros o hacia los conectores de la HAT (J1, J7–J12, J3, J11). Integración con NetlistBuilder.
Tests: montar el reto 4 (LED externo con resistencia a GPIO14 por J7) y el divisor de la LDR del reto 9, y comprobar las tensiones resueltas.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/protoboard.
```

**Comprueba:** los tests en verde. Dibuja en papel el montaje del test y compáralo con el código.

**Deberías entender:** cómo están conectados por dentro los agujeros de una protoboard y cómo se convierten en nodos.

### E13. Autorouter A\* (la IA del proyecto)

```text
Etapa 13: autorouter de cables con A* sobre la rejilla de la protoboard (docs/requisitos.md §2.3), detrás de una interfaz IRouter (patrón Strategy).
- Coste: longitud + penalización por cruce con otros cables + penalización por giros.
- Restricción: no pasar por agujeros ocupados.
- Heurística admisible.
Añade un router simple (en L) como segunda estrategia para comparar.
Tests: camino óptimo en casos pequeños calculables a mano; sin camino posible → resultado vacío; la heurística no sobreestima.
Explícame por qué A* encuentra el óptimo y qué información del entorno recibe (requisito R8).
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/astar.
```

**Comprueba:**

- Los tests en verde.
- Imprime en consola una rejilla con el camino encontrado y compruébalo visualmente.

**Deberías entender:**

- A\*: f = g + h.
- Por qué la heurística debe ser admisible.
- Strategy.

---

## Bloque 5. Unity (presentación)

### E14. Escena base: Pi + HAT interactiva

```text
Etapa 14: primera escena 2D en Unity. Sprites provisionales de la Raspberry Pi con la IMAT HAT (pueden ser formas simples); vistas que observan el dominio (Observer): LEDs D1–D8 que se iluminan según el modelo y pulsadores SW1–SW4 clicables. La simulación avanza con SimulatorFacade desde un MonoBehaviour.
Programa fijo de prueba: el de "LED mientras pulso".
Las vistas no contienen lógica de simulación.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/unity-escena-base.
```

**Comprueba:** al darle a Play y pulsar SW4 con el ratón se encienden los LEDs que tocan. Mira en la jerarquía que las vistas solo leen el modelo.

**Deberías entender:** cómo se comunica la presentación con el dominio sin que el dominio conozca Unity.

### E15. Editor de código en el juego

```text
Etapa 15: panel de editor de código (TextMeshPro) con botones Ejecutar / Parar / Reiniciar, consola de salida (print) y errores con la línea marcada. Opcional: resaltar la línea en ejecución.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/unity-editor-codigo.
```

**Comprueba:**

- Escribe el reto 1 en el juego y mira cómo se enciende D1.
- Escribe un programa con un error de sintaxis y comprueba el mensaje.

**Deberías entender:** el recorrido desde el texto que escribes hasta el LED que se enciende, pasando por todas las capas.

### E16. Protoboard visual y cables con A\*

```text
Etapa 16: protoboard visual: paleta de componentes (desde ComponentFactory), arrastrar y soltar en los agujeros, cables entre puntos trazados por el A* y dibujados con LineRenderer, borrar y mover componentes. Validación visual de conexiones.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/unity-protoboard.
```

**Comprueba:**

- Monta el reto 4 a mano.
- Pon el LED sin resistencia y comprueba que la simulación detecta el fallo.

**Deberías entender:** cómo se traduce el ratón (coordenadas de pantalla) a la rejilla del modelo.

### E17. Flujo de juego completo

```text
Etapa 17: loop de juego completo (requisito R4): menú principal, selección de retos con progreso, enunciado de cada reto, pantallas de victoria y derrota (con la causa física: "LED quemado: 12 mA > 20 mA máx…", un efecto visual del componente dañado), reintentar o siguiente, y reinicio limpio sin estado residual.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/unity-flujo-juego.
```

**Comprueba:**

- Juega los retos 1–4 seguidos: gana uno, pierde otro y reinicia.
- Comprueba que todo vuelve al estado inicial al reiniciar.

**Deberías entender:** cómo la máquina de estados del dominio gobierna las pantallas.

### E18. Servo, potenciómetro y LDR visuales

```text
Etapa 18: vistas del servo (brazo que gira con la velocidad del modelo), del potenciómetro RV1 (girable con el ratón), de la LDR (control deslizante de luz en lux) y del brillo de los PWMLED. Completa los datos de los retos 5, 7, 8 y 9 y comprueba que todos los retos se pueden jugar.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/unity-analogico.
```

**Comprueba:** juega los 9 retos (o el mínimo jugable: 1–5 más 8 o 9).

**Deberías entender:** el recorrido completo de un dato analógico: lux → resistencia → divisor → ADC → programa → PWM → brillo.

---

## Bloque 6. Cierre y entrega 2

### E19. Pulido y robustez

```text
Etapa 19: pulido.
- Arte provisional sustituido por sprites mejores (basados en la foto de la placa, sin usar material con licencia restrictiva) y sonido básico.
- Ayuda por reto.
- Revisión de robustez: programas maliciosos o absurdos (bucles infinitos, recursión profunda, números enormes) no pueden colgar ni cerrar el juego.
Haz una lista de riesgos de crash y pruébalos uno a uno.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: feat/pulido.
```

**Comprueba:** intenta romper el juego durante 10 minutos con programas raros.

### E20. Builds y prueba en otro equipo

```text
Etapa 20: build para PC (y para Mac si es posible). Repasa la checklist de requisitos R1–R8 de docs/requisitos.md e indica cómo se cumple cada uno con evidencias. Dime qué probar en otro ordenador.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: chore/build.
```

**Comprueba:** el build funciona en otro ordenador sin Unity instalado. Juega una partida completa.

### E21. Documentación final y entrega 2

```text
Etapa 21: entrega 2 (docs/requisitos.md §1.3).
1. Actualiza todos los diagramas UML al código final.
2. Prepara el .zip 2_NombreApellido1_NombreApellido2_IMATPiSimulator_26_27 con el build y el proyecto Unity, sin Library, Temp, UserSettings, Logs ni Obj.
3. Comprueba el contenido del .zip antes de darlo por bueno.
Sigue las reglas de docs/hoja_de_ruta.md. Rama: docs/entrega-2.
```

**Comprueba:**

- Descomprime el `.zip` en otra carpeta, abre el proyecto con Unity 6000.6.8f1 y ejecuta el build.

---

## Ampliaciones opcionales (solo si sobra tiempo)

| Id | Ampliación | Cuándo |
|----|------------|--------|
| A1 | Motor DC con L293 + reto de posición con PID ajustado por un **algoritmo genético** (segunda IA; los pines ya están en docs/hardware.md §3.5) | Tras E18 |
| A2 | Agente **ML-Agents** entrenado por nosotros (bonus del enunciado) | Tras A1 |
| A3 | Más componentes: buzzer, FSR, sensor IR | Cuando quieras |

---

## Seguimiento

| Etapa | Hecha | Commit / notas |
|-------|-------|----------------|
| E0.1 Repo y GitHub | ☐ | |
| E0.2 Proyecto Unity | ☐ | |
| E1 Solver nodal | ☐ | |
| E2 GPIO y pulsador | ☐ | |
| E3 LED y fallos | ☐ | |
| E4 Componentes y HAT | ☐ | |
| E5 PWM, servo, ADC | ☐ | |
| **E-UML Entrega 1** | ☐ | antes del 03/12 |
| E6 Lexer | ☐ | |
| E7 Parser y AST | ☐ | |
| E8 Ejecución | ☐ | |
| E9 API gpiozero | ☐ | |
| E10 Motor | ☐ | |
| E11 Retos | ☐ | |
| E12 Protoboard | ☐ | |
| E13 A\* | ☐ | |
| E14 Escena base | ☐ | |
| E15 Editor de código | ☐ | |
| E16 Protoboard visual | ☐ | |
| E17 Flujo de juego | ☐ | |
| E18 Analógico visual | ☐ | |
| E19 Pulido | ☐ | |
| E20 Builds | ☐ | |
| **E21 Entrega 2** | ☐ | antes del 10/01 |
