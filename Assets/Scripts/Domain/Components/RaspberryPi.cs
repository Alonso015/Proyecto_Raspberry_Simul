using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Raspberry Pi vista desde el conector de 40 pines: 28 GPIO (BCM 0..27) y los raíles de
    /// 3,3 V, 5 V y GND (docs/hardware.md §1).
    /// </summary>
    /// <remarks>
    /// Pines: "GPIO0".."GPIO27", "3V3", "5V" y "GND". La numeración es BCM, no la física del
    /// conector (GPIO7 es el pin físico 26). Los conflictos de funciones (SPI, PWM...) llegan en E5.
    /// </remarks>
    public sealed class RaspberryPi : Component
    {
        public const int GpioCount = 28;

        private readonly GpioPin[] _gpio = new GpioPin[GpioCount];

        public RaspberryPi(string id = "Pi") : base(id, ComponentTypes.RaspberryPi)
        {
            Rail3V3 = new VoltageRail("3V3", 3.3);
            Rail5V = new VoltageRail("5V", 5.0);
            Ground = VoltageRail.Ground();
            for (int bcm = 0; bcm < GpioCount; bcm++)
            {
                _gpio[bcm] = new GpioPin(bcm);
                AddPin("GPIO" + bcm, _gpio[bcm].Pin);
            }
            AddPin("3V3", Rail3V3.Terminal);
            AddPin("5V", Rail5V.Terminal);
            AddPin("GND", Ground.Terminal);
        }

        public VoltageRail Rail3V3 { get; }
        public VoltageRail Rail5V { get; }
        public VoltageRail Ground { get; }

        public GpioPin Gpio(int bcm)
        {
            if (bcm < 0 || bcm >= GpioCount)
                throw new ArgumentOutOfRangeException(nameof(bcm), bcm, "Los GPIO van de 0 a 27 (numeración BCM).");
            return _gpio[bcm];
        }

        public IReadOnlyList<GpioPin> AllGpio => _gpio;

        public override IEnumerable<IElectricalElement> ElectricalElements()
        {
            yield return Rail3V3;
            yield return Rail5V;
            yield return Ground;
            foreach (var g in _gpio) yield return g;
        }

        public override void Reset()
        {
            foreach (var g in _gpio) g.Reset();
        }
    }
}
