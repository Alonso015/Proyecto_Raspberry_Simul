using NUnit.Framework;

namespace ImatPiSim.Domain.Tests
{
    /// <summary>
    /// Test trivial de la etapa E0.2: comprueba que los tests del dominio se compilan y ejecutan
    /// tanto en el Test Runner de Unity como con <c>dotnet test</c>. Solo usa NUnit, nada de Unity.
    /// </summary>
    public class DomainInfoTests
    {
        [Test]
        public void DomainAssembly_IsReachable_AndHasAVersion()
        {
            Assert.That(DomainInfo.Name, Does.Contain("IMAT Pi Simulator"));
            Assert.That(DomainInfo.Version, Is.Not.Empty);
        }
    }
}
