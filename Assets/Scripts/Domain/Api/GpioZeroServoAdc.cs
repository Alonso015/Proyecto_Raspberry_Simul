using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Interpreter.Runtime;

namespace ImatPiSim.Domain.Api
{
    /// <summary>
    /// <c>Servo</c> y <c>AngularServo</c> de gpiozero: generan el PWM de control de un servo.
    /// </summary>
    /// <remarks>
    /// <para>Código → pin: <c>servo.angle = 45</c> se traduce a un ancho de pulso entre
    /// <c>min_pulse_width</c> y <c>max_pulse_width</c>, repetido cada <c>frame_width</c> (20 ms →
    /// 50 Hz). El factor de servicio es pulso / periodo.</para>
    /// <para><b>Valores por defecto de gpiozero: 1 ms y 2 ms.</b> Los servos del laboratorio
    /// esperan 0,5–2,5 ms (práctica 4). Con los valores por defecto, el servo real (y el
    /// simulado) solo recorre de 45° a 135°: por eso los programas de clase indican
    /// <c>min_pulse_width=0.0005, max_pulse_width=0.0025</c>.</para>
    /// <para><c>detach()</c> deja de enviar pulsos: el servo se queda donde está.</para>
    /// </remarks>
    internal sealed class GzServo : GzDevice
    {
        private readonly IPin _pin;
        private readonly double _minPulse, _maxPulse, _frame;
        private readonly double _minAngle, _maxAngle;
        private readonly bool _angular;
        private double? _value;   // −1..1; null = desconectado (detach)

        public GzServo(GpioZeroContext ctx, string typeName, object[] args, IReadOnlyDictionary<string, object> kw)
            : base(ctx, typeName)
        {
            _angular = typeName == "AngularServo";
            var p = _angular
                ? Params.Bind(typeName, args, kw, ("pin", Params.Required), ("initial_angle", 0.0), ("min_angle", -90L),
                              ("max_angle", 90L), ("min_pulse_width", 1.0 / 1000), ("max_pulse_width", 2.0 / 1000),
                              ("frame_width", 20.0 / 1000))
                : Params.Bind(typeName, args, kw, ("pin", Params.Required), ("initial_value", 0.0),
                              ("min_pulse_width", 1.0 / 1000), ("max_pulse_width", 2.0 / 1000), ("frame_width", 20.0 / 1000));
            _minPulse = p.Double("min_pulse_width");
            _maxPulse = p.Double("max_pulse_width");
            _frame = p.Double("frame_width");
            if (!(_minPulse > 0) || !(_maxPulse > _minPulse) || !(_frame >= _maxPulse))
                throw PyException.ValueError("hace falta 0 < min_pulse_width < max_pulse_width <= frame_width");
            if (_angular)
            {
                _minAngle = p.Double("min_angle");
                _maxAngle = p.Double("max_angle");
                if (_minAngle == _maxAngle) throw PyException.ValueError("min_angle y max_angle no pueden ser iguales");
            }

            int bcm = PinNumbers.Parse(p["pin"]);
            _pin = ctx.Pins.Reserve(bcm, $"{typeName}({bcm})");
            if (_angular)
            {
                var initial = p["initial_angle"];
                if (initial == null) Detach();
                else SetAngle(PyOps.ToDouble(initial, "initial_angle"));
            }
            else
            {
                var initial = p["initial_value"];
                if (initial == null) Detach();
                else SetValue(PyOps.ToDouble(initial, "initial_value"));
            }

            Method("min", a => { SetValue(-1); return null; });
            Method("mid", a => { SetValue(0); return null; });
            Method("max", a => { SetValue(1); return null; });
            Method("detach", a => { Detach(); return null; });
            Property("value", () => _value, v => { if (v == null) Detach(); else SetValue(PyOps.ToDouble(v, "value")); });
            Property("pulse_width", () => _value == null ? null : (object)PulseFor(_value.Value));
            Property("frame_width", () => _frame);
            Property("min_pulse_width", () => _minPulse);
            Property("max_pulse_width", () => _maxPulse);
            Property("pin", () => "GPIO" + _pin.Number);
            if (_angular)
            {
                Property("angle", () => _value == null ? null : (object)AngleFor(_value.Value),
                         v => { if (v == null) Detach(); else SetAngle(PyOps.ToDouble(v, "angle")); });
                Property("min_angle", () => _minAngle);
                Property("max_angle", () => _maxAngle);
            }
        }

        private double PulseFor(double value) => _minPulse + (value + 1) / 2 * (_maxPulse - _minPulse);
        private double AngleFor(double value) => _minAngle + (value + 1) / 2 * (_maxAngle - _minAngle);

        private void SetAngle(double angle)
        {
            double lo = Math.Min(_minAngle, _maxAngle), hi = Math.Max(_minAngle, _maxAngle);
            if (angle < lo - 1e-9 || angle > hi + 1e-9)
                throw new PyException("OutputDeviceBadValue", $"el ángulo debe estar entre {PyOps.Str(lo)} y {PyOps.Str(hi)}, no {PyOps.Str(angle)}");
            SetValue((angle - _minAngle) / (_maxAngle - _minAngle) * 2 - 1);
        }

        private void SetValue(double v)
        {
            if (v < -1 - 1e-9 || v > 1 + 1e-9)
                throw new PyException("OutputDeviceBadValue", $"el valor de {TypeName} debe estar entre -1 y 1, no {PyOps.Str(v)}");
            _value = Math.Max(-1, Math.Min(1, v));
            _pin.SetPwm(1.0 / _frame, PulseFor(_value.Value) / _frame);
        }

        private void Detach()
        {
            _value = null;
            _pin.Write(false);   // sin pulsos: el servo mantiene su posición
        }

        protected override void OnClose() => Ctx.Pins.Release(_pin);
    }

    /// <summary>
    /// <c>MCP3008(channel=0)</c>: lee un canal del conversor A/D de la IMAT HAT.
    /// </summary>
    /// <remarks>
    /// <c>value</c> = código / 1023 (0..1) y <c>voltage</c> = value · 3,3 V, como en la práctica 5.
    /// La lectura viene del último cálculo del circuito. Si el SPI está desactivado, la lectura
    /// falla con <c>SPIError</c>.
    /// </remarks>
    internal sealed class GzMcp3008 : GzDevice
    {
        private readonly int _channel;
        private readonly double _maxVoltage;

        public GzMcp3008(GpioZeroContext ctx, object[] args, IReadOnlyDictionary<string, object> kw)
            : base(ctx, "MCP3008")
        {
            var p = Params.Bind("MCP3008", args, kw, ("channel", 0L), ("differential", false), ("max_voltage", 3.3),
                                ("clock_pin", null), ("mosi_pin", null), ("miso_pin", null), ("select_pin", null));
            if (ctx.Adc == null)
                throw new PyException("SPIError", "no hay ningún MCP3008 conectado (hace falta la IMAT HAT)");
            _channel = (int)PyOps.ToLong(p["channel"], "channel");
            if (_channel < 0 || _channel > 7) throw PyException.ValueError("el canal del MCP3008 va de 0 a 7");
            if (p.Bool("differential")) throw PyException.ValueError("el modo diferencial no está soportado en el simulador");
            _maxVoltage = p.Double("max_voltage");

            Property("value", () => Read());
            Property("voltage", () => Read() * _maxVoltage);
            Property("raw_value", () => (long)Math.Round(Read() * Components.Mcp3008.MaxCode));
            Property("channel", () => (long)_channel);
            Property("max_voltage", () => _maxVoltage);
        }

        private double Read()
        {
            try { return Ctx.Adc.ReadValue(_channel); }
            catch (InvalidOperationException ex) { throw new PyException("SPIError", ex.Message); }
        }

        protected override void OnClose() { }
    }
}
