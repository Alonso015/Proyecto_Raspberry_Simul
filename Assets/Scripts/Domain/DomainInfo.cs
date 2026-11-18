namespace ImatPiSim.Domain
{
    /// <summary>
    /// Información del ensamblado de dominio. Existe para comprobar que la estructura
    /// (asmdef + proyecto dotnet de tests) compila y se puede probar en los dos entornos.
    /// </summary>
    public static class DomainInfo
    {
        public const string Name = "IMAT Pi Simulator - Dominio";

        /// <summary>Versión del modelo de dominio; se incrementa al cerrar cada bloque de la hoja de ruta.</summary>
        public const string Version = "0.0.1";
    }
}
