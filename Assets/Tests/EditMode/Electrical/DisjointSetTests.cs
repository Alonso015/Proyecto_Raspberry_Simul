using ImatPiSim.Domain.Electrical;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Electrical
{
    public class DisjointSetTests
    {
        [Test]
        public void UnionesTransitivas_AgrupanEnUnMismoConjunto()
        {
            var ds = new DisjointSet<string>();
            ds.Union("A", "B");
            ds.Union("B", "C");
            ds.Add("D");

            Assert.That(ds.AreConnected("A", "C"), Is.True);
            Assert.That(ds.AreConnected("A", "D"), Is.False);
        }

        [Test]
        public void UnirDosVeces_DevuelveFalseLaSegunda()
        {
            var ds = new DisjointSet<int>();
            Assert.That(ds.Union(1, 2), Is.True);
            Assert.That(ds.Union(2, 1), Is.False);
        }

        [Test]
        public void CadenaLarga_SigueConectadaDeExtremoAExtremo()
        {
            var ds = new DisjointSet<int>();
            for (int i = 0; i < 1000; i++) ds.Union(i, i + 1);
            Assert.That(ds.AreConnected(0, 1000), Is.True);
        }
    }
}
