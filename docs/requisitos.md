# Requisitos y enfoque: IMAT Pi Simulator

Fuente: enunciado del proyecto final de Paradigmas de la Programación, curso 2026/27 ("Game or Sim Jam"). El PDF no se versiona.

## 1. Requisitos del enunciado

### 1.1 Obligatorios

| Id | Requisito | Cómo lo cumplimos |
|----|-----------|-------------------|
| R1 | Videojuego o simulación en **Unity 6000.0.85f2** | Proyecto creado con esa versión exacta |
| R2 | Desarrollo **por parejas** | Pendiente: confirmar pareja y reparto |
| R3 | Incluir **IA programada**. En una simulación, un algoritmo de IA (camino óptimo, algoritmo genético, clasificación…) | Autorouter de cables con **A\*** (§2.3) |
| R4 | **Loop cerrado**: Play → jugar → victoria / derrota → jugar otra vez, con reinicio correcto | **Modo retos** (§2.2) |
| R5 | Compilable y jugable en otros dispositivos (PC o Mac), **sin crashes** | Builds periódicos desde las primeras etapas |
| R6 | **Diseño UML** de los sistemas principales, detallando los patrones de diseño | Entrega 1 (§3) |
| R7 | Gran peso de la **calidad y complejidad del código**, la POO y los patrones | Arquitectura por capas (§2.4); patrones solo donde resuelvan un problema real |
| R8 | La información del entorno que recibe la IA debe ser la adecuada para su cometido | El A\* trabaja sobre el modelo lógico de la protoboard, no sobre la escena visual |

### 1.2 Valorables (no obligatorios)

- Arte y sonido. Pesan menos que el código; se pueden usar assets gratuitos sin licencia restrictiva.
- **Bonus:** agente entrenado por nosotros con ML-Agents.
- No se aconseja multijugador.

### 1.3 Entregas

| Entrega | Fecha | Contenido |
|---------|-------|-----------|
| 1 | **03/12/2026** | `.zip` llamado `1_NombreApellido1_NombreApellido2_NombreProyecto_26_27`, con: descripción del proyecto (objetivo, funcionamiento, esquema general) y **diseño UML de la arquitectura** |
| 2 | **10/01/2027** (el PDF la rotula también como "Entrega 1"; por la fecha es la segunda) | `.zip` llamado `2_NombreApellido1_NombreApellido2_NombreProyecto_26_27`, con: build jugable para PC/Mac; proyecto Unity completo **sin** `Library`, `Temp`, `UserSettings`, `Logs` ni `Obj`; si hay ML-Agents, el proyecto de entrenamiento y el `.yaml` |

---

## 2. Enfoque elegido

### 2.1 Concepto

Simulador educativo de Raspberry Pi + IMAT HAT con **modo retos**:

- Cada nivel plantea un objetivo de electrónica real, basado en las prácticas de Sistemas Electrónicos.
- El jugador **monta** el circuito y **programa** el comportamiento.
- Un verificador decide si el reto se ha superado.

El hardware simulado se basa en lo documentado en [hardware.md](hardware.md).

### 2.2 Loop de juego (R4)

```
Menú ─► Selección de reto ─► Montaje + programación ─► Ejecutar
                                                        │
                       ┌────────────────────────────────┼─────────────────────────┐
                       ▼                                ▼                         ▼
                Victoria: el verificador      Derrota: fallo eléctrico     Derrota: tiempo /
                comprueba el objetivo         (componente quemado, 5 V     presupuesto agotado
                                              en GPIO, cortocircuito…)
                       │                                │                         │
                       └──────────────► Reintentar / Siguiente reto ◄─────────────┘
```

**Victoria:** el verificador inyecta secuencias de entradas (pulsaciones, valores del ADC) y comprueba que las salidas cumplen la especificación del reto.

**Derrota:** consecuencias de errores eléctricos reales, por ejemplo:

- LED sin resistencia limitadora (corriente excesiva → LED quemado).
- Lógica de 5 V conectada a un GPIO.
- Cortocircuito entre alimentación y GPIO o GPIO y GND.
- Corriente por GPIO por encima del límite.
- Conflictos de pines documentados (pulsador en GPIO7 con SPI activo, GPIO18 con PWM y SPI1…).
- Opcional: límite de tiempo o de presupuesto de componentes.

La lista cerrada de retos está en §2.8.

### 2.3 IA (R3, R8)

**Núcleo: autorouter de cables con A\***

- La protoboard se modela como una rejilla o grafo de agujeros.
- Las tiras conectadas internamente equivalen a un mismo nodo eléctrico.
- Al pedir una conexión, la IA calcula el recorrido óptimo del cable:
  - Esquiva agujeros ocupados.
  - Minimiza la longitud y el número de cruces con otros cables.
- La entrada de la IA es el modelo lógico de la protoboard (ocupación y nodos), no la escena de Unity (R8).

**Ampliación si hay tiempo: algoritmo genético para ajustar un PID**

- Reto de posicionar un motor DC.
- El GA busca Kp, Ki y Kd.
- Conecta con Sistemas de Control.

**Bonus opcional:** agente ML-Agents que aprende a controlar el servo o el motor.

### 2.4 Arquitectura (borrador para el UML)

| Capa | Responsabilidad |
|------|-----------------|
| Representación visual | GameObjects, prefabs, cables, animaciones |
| Componentes | Modelo lógico de cada componente (LED, pulsador, servo, MCP3008…) |
| Modelo eléctrico | Grafo de nodos y conexiones, propagación de estados, validación eléctrica |
| Conexiones | Protoboard, pines, cables, autorouter A\* |
| Ejecución de programas | Programa del usuario → operaciones sobre GPIO simulados |
| Retos | Definición de objetivos, verificador, condiciones de victoria y derrota |
| UI y flujo de juego | Menús, selección de reto, estados del loop |

Los patrones (Singleton, Factory, Bridge, Facade, Decorator, Flyweight…) se decidirán al diseñar el UML, solo donde resuelvan un problema concreto.

### 2.5 Programación de los componentes

**Decidido (12/11/2026): intérprete propio, escrito en C#, de un subconjunto de Python con la API de gpiozero.**

Alternativas descartadas:

| Opción | Motivo del descarte |
|--------|---------------------|
| Bloques visuales | Poca fidelidad a lo que se programa en la Pi; el mérito recae en la UI |
| Python real incrustado (IronPython / Python.NET) | Ejecuta código arbitrario; problemas de hilos con el bucle de Unity y de portabilidad del build; el mérito sería de una librería ajena |
| Reglas simplificadas | No expresan bien temporizadores, flancos ni PWM |

**Motivos de la elección:**

- Fidelidad: el jugador escribe código como el del laboratorio (`LED(20)`, `Button(19).when_pressed = ...`).
- Seguridad: solo existe lo que implementamos.
- Funciona igual en PC y Mac.

**Modelo de ejecución:**

- El intérprete es cooperativo: ejecuta un número acotado de instrucciones por tick de simulación.
- `sleep(t)` suspende el programa durante `t` segundos de tiempo simulado sin bloquear Unity.
- `while True` actúa como bucle de scan.
- Los callbacks (`when_pressed`, `when_released`) se encolan y se ejecutan entre instrucciones.

**Alcance mínimo del lenguaje (ampliable por etapas):**

| Incluido | Fuera del alcance inicial |
|----------|---------------------------|
| Enteros, decimales, booleanos, cadenas; variables | Clases de usuario |
| Operadores aritméticos, de comparación, lógicos y a nivel de bit | `import` de librerías arbitrarias (solo `from gpiozero import ...` y `from time import sleep`, que el intérprete reconoce) |
| `if` / `elif` / `else`, `while`, `for ... in range(...)`, `break` | Listas por comprensión, lanzar excepciones (`raise`) y clases de excepción propias |
| `try` / `except [Tipo] [as e]` / `finally` (básico, decidido el 18/11/2026): el botón **Parar** lanza `KeyboardInterrupt`, como Ctrl+C en la Pi | `except (A, B)`, `else` de `try` |
| `def`, `return`, llamadas a funciones, `lambda` simple para callbacks | Generadores, decoradores, `async` |
| `sleep()`, `print()` | |
| `LED`, `PWMLED`, `LEDBoard`, `Button`, `AngularServo`, `MCP3008` con sus métodos y propiedades básicos | Resto de clases de gpiozero |

**Patrones candidatos** (se confirmarán al hacer el UML):

- **Interpreter + Composite:** el árbol sintáctico del programa.
- **Visitor:** evaluación y análisis previo del árbol.
- **Bridge:** entre la API de gpiozero y los pines simulados, como hace la `pin_factory` real de gpiozero.
- **Facade:** punto de acceso del intérprete al simulador.

### 2.6 Modelo eléctrico

**Decidido (12/11/2026): modelo híbrido.**

- Análisis nodal en continua para la red pasiva.
- Modelos de comportamiento para chips y actuadores.

Alternativas descartadas:

| Opción | Motivo del descarte |
|--------|---------------------|
| Solo lógico (HIGH / LOW / flotante + reglas) | No calcula corrientes: las derrotas serían reglas ad hoc. No resuelve divisores de tensión, necesarios para los retos con LDR o potenciómetro y el MCP3008 |
| Análisis nodal completo con transitorios (tipo SPICE) | Coste desproporcionado para el alcance del proyecto |

**Red pasiva: análisis nodal.**

- Se plantea y resuelve **G·V = I** (eliminación gaussiana) cada vez que cambia el circuito o el estado de un componente.
- Cada componente aporta ("estampa") su contribución a la matriz:

| Elemento | Modelo |
|----------|--------|
| GPIO como salida | Fuente de 3,3 V o 0 V con pequeña resistencia interna |
| GPIO como entrada | Alta impedancia; pull-up o pull-down interna opcional |
| Resistencia, potenciómetro, LDR | Conductancia (la LDR y el potenciómetro, variables) |
| Pulsador | Interruptor: resistencia muy baja cerrado, conductancia nula abierto |
| LED | Diodo lineal a trozos: no conduce por debajo de su tensión umbral; por encima, umbral + pequeña resistencia. Se itera hasta que el estado de los diodos es coherente |
| Raíles de 3,3 V, 5 V, GND | Nodos de tensión fija |

**Chips y actuadores: modelos de comportamiento.**

| Componente | Modelo |
|------------|--------|
| MCP3008 | Lee la tensión del nodo de cada canal y la cuantiza a 10 bits sobre 3,3 V |
| PWMLED | Brillo proporcional al factor de servicio. No se simula la forma de onda punto a punto |
| Servo | Posición objetivo a partir del ancho de pulso; movimiento hacia ella con velocidad limitada |
| L293D + motor DC | Sentido según los pines de dirección; velocidad según el factor de servicio |

**Detección de fallos (estados de derrota)** a partir de las magnitudes calculadas:

- Corriente de LED por encima de su máximo → LED quemado.
- Corriente por GPIO por encima del límite → GPIO dañado.
- Corriente desmesurada entre raíles → cortocircuito.
- Tensión de más de 3,3 V en un GPIO → Pi dañada.

**Supuestos de la simulación** (se documentarán como aproximaciones, no como datos de la IMAT HAT):

| Supuesto | Valor provisional | Motivo |
|----------|-------------------|--------|
| Tensión umbral del LED | ~2 V | Valor típico, no medido |
| Corriente máxima del LED | ~20 mA | Valor típico, no medido |
| Valor de R1–R8 de la HAT | **270 Ω** | Ya no es supuesto: confirmado por el esquema (hardware.md §3.2) |
| Límite de corriente por GPIO | **7 mA** (decidido 17/11/2026) | Valor de las diapositivas (hardware.md §1.2). Se descartan los 4 mA de la práctica 5 porque los LEDs de la HAT consumen ≈ 4,8 mA (hardware.md §3.2). Un único umbral: por encima, el GPIO se daña |
| Resistencia de salida de un GPIO | 40 Ω (E2, aceptado el 18/11/2026) | No está en las fuentes. Modela que la tensión del pin cae un poco al entregar corriente (≈ 3,17 V con 1 kΩ a GND) |
| Pull-up / pull-down interna | 50 kΩ (E2, aceptado el 18/11/2026) | No está en las fuentes. Solo importa que sea mucho mayor que el contacto de un pulsador y mucho menor que la impedancia de entrada |
| Umbrales de lectura de una entrada | LOW ≤ 0,8 V; HIGH ≥ 2,0 V; entre ambos, *indefinido* (E2, aceptado el 18/11/2026) | No está en las fuentes. Valores típicos de lógica de 3,3 V. En la zona indefinida se mantiene la última lectura (aproxima la histéresis) |
| Resistencia del contacto de un pulsador | 0,1 Ω (E2, aceptado el 18/11/2026) | No está en las fuentes. Pulsador suelto = circuito abierto ideal; sin rebotes |
| Resistencia serie del LED en conducción | 10 Ω (E3, aceptado el 18/11/2026) | No está en las fuentes. Modelo lineal a trozos: V_F + R_S; valor típico de un LED de 5 mm |
| Margen de sobretensión en un GPIO | 3,3 V + 0,1 V (E3, aceptado el 18/11/2026) | Las fuentes dicen que más de 3,3 V daña la Pi. El margen evita confundir el redondeo numérico con un fallo |
| Resistencia de un puente de soldadura unido (JP1–JP3) | 1 mΩ (E4, aceptado el 18/11/2026) | No está en las fuentes. Prácticamente un cable |
| Resistencia mínima de cada tramo del potenciómetro | 0,01 Ω (E4, aceptado el 18/11/2026) | Evita un cortocircuito ideal en los extremos del recorrido |
| Resistencia de la LDR en oscuridad total | 1 MΩ (E4, aceptado el 18/11/2026) | El datasheet solo da un mínimo de 0,5 MΩ. Fuera de la tabla de la práctica 5 (40–1200 lx) se extrapola en log-log |
| Velocidad máxima del servo | 600 °/s (E5, aceptado el 18/11/2026) | No está en las fuentes. Del orden de 0,1 s cada 60°, típico de servos pequeños |
| Alimentación mínima para que el servo se mueva | 4 V entre VCC y GND (E5, aceptado el 18/11/2026) | No está en las fuentes. Sin 5 V el servo no se mueve; no se modela su consumo |
| Conversor MCP3008 | Ideal: código = ⌊1024·V/3,3⌋ en 0..1023, sin ruido ni offset; más de 3,4 V en un canal = daño (E5) | Fórmula de 10 bits sobre V_ref (F4, F5); el margen es el mismo supuesto que en los GPIO |
| SPI0 al arrancar | Desactivado (E5) | Como en Raspberry Pi OS: la práctica 5 pide activarlo con `raspi-config` |

### 2.7 Representación visual

**Decidido (12/11/2026): 2D con vista cenital y sprites.**

Motivos:

- La protoboard es una rejilla: colocar componentes y cables es directo, y los caminos del A\* se ven con claridad.
- El enunciado prioriza el código sobre los assets, y el proyecto ya tiene tres piezas exigentes (intérprete, solver eléctrico, A\*).

Alternativas descartadas:

| Opción | Motivo del descarte |
|--------|---------------------|
| 3D ortográfico | Más vistoso, pero exige modelar la Pi, la HAT y los componentes, y añade selección por rayos, iluminación y materiales |
| 3D libre | Colocar cables con precisión es incómodo para el jugador y costoso de programar |

Condición de diseño: la representación visual queda separada de la lógica (§2.4). Así se podrán hacer mejoras estéticas posteriores (iluminación 2D con URP, animaciones) sin tocar el resto de capas.

### 2.8 Componentes y retos

**Decidido (12/11/2026).**

#### Componentes del núcleo (obligatorios para la entrega 2)

| Componente | Modelo | Origen |
|------------|--------|--------|
| Raspberry Pi + IMAT HAT: 8 LEDs (D1–D8), 4 pulsadores (SW1–SW4) con condensador antirrebote, potenciómetro en CH7, MCP3008 | Nodal + comportamiento | [hardware.md](hardware.md) |
| Protoboard y cables | Grafo de nodos; A\* para el trazado | — |
| Resistencias de valor seleccionable (p. ej. 220 Ω, 330 Ω, 1 kΩ, 10 kΩ) | Conductancia | Prácticas 3 y 5 |
| LED externo | Diodo lineal a trozos | — |
| Pulsador externo | Interruptor | Práctica 3 |
| Fotorresistencia (LDR) | Resistencia variable según la luz (curva de la práctica 5) | Práctica 5 |
| Servo en DIG (SERVO), GPIO14 | Comportamiento PWM | Práctica 4 |

#### Ampliaciones (solo si sobra tiempo)

| Componente | Motivo para dejarlo fuera del núcleo |
|------------|--------------------------------------|
| L293D + motor DC (y GA del PID) | Coste de tiempo. Los pines ya se conocen (hardware.md §3.5) |
| Buzzer, FSR, sensor IR | Aportan poco nuevo respecto a la LDR |
| Amplificador operacional MCP6002 | Complica el solver; la LDR se usa con un divisor simple |

#### Retos (en orden de dificultad)

| # | Reto | Qué enseña | Derrota posible |
|---|------|------------|-----------------|
| 1 | **Hola LED**: encender D1 (GPIO20) | Primer programa, `LED.on()` | Ninguna (tutorial) |
| 2 | **Mientras pulso**: LEDs pares encendidos solo mientras se pulsa SW4 (GPIO19) | Entrada por nivel | Ninguna |
| 3 | **Interruptor**: cada pulsación de SW2 alterna los LEDs impares | Flanco vs. nivel | El verificador detecta conmutaciones múltiples por pulsación |
| 4 | **Mi primer circuito**: LED externo con resistencia en GPIO14 sobre la protoboard | Montaje, resistencia limitadora, A\* | LED quemado (sin resistencia o resistencia demasiado baja) |
| 5 | **Pulsador externo**: pulsador en pull-up en GPIO14 que conmuta los LEDs | Pull-up, nivel en reposo | Cortocircuito (3,3 V directo a GND) |
| 6 | **Contador binario**: 8 bits en D1–D8; SW1 suma, SW2 resta, SW3 reinicia | Operaciones a nivel de bit, varias entradas | Conflicto GPIO7 / SPI si el SPI está activo |
| 7 | **Nivel del potenciómetro**: barra de LEDs proporcional a CH7 | ADC, cuantización, escalado | — |
| 8 | **Barrera**: servo que abre y cierra con SW4 y se cierra solo a los 10 s | PWM de servo, temporización no bloqueante | Tiempo (la barrera no se cierra a tiempo) |
| 9 | **Luz automática**: divisor con LDR → ADC → LED con PWM inverso a la luz | Divisor de tensión, sensor, PWM | GPIO o ADC dañado (divisor alimentado a 5 V) |

**Progresión:**

- Retos 1–3: solo programación.
- Retos 4–5: introducen el montaje y el A\*.
- Retos 6–9: combinan las dos cosas.

El orden coincide con la implementación por etapas: intérprete → solver y protoboard → ADC y PWM.

**Mínimo jugable:** retos 1–5 más uno de los finales (8 o 9). Con eso el loop queda completo y demostrable aunque no dé tiempo a los 9.

---

## 3. Decisiones pendientes

Decisiones ya tomadas:

- Modo retos + IA con A\* (§2.2, §2.3).
- Subconjunto de Python (§2.5).
- Modelo eléctrico híbrido (§2.6).
- 2D cenital (§2.7).
- Componentes y retos (§2.8).

| Decisión | Opciones | Antes de |
|----------|----------|----------|
| Pareja y reparto del trabajo | — | Entrega 1 |
| Ampliaciones (GA del PID, ML-Agents) | — | Tras la entrega 1 |
