using System.Collections.Generic;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Domain.Wiring;
using UnityEngine;
using DomainComponent = ImatPiSim.Domain.Components.Component;

namespace ImatPiSim.Presentation.Assembly
{
    /// <summary>
    /// Vista de un componente pinchado en la protoboard. Se coloca entre su primer y su último
    /// agujero y gira según la dirección de la colocación. Si es un LED o un pulsador, reutiliza
    /// <see cref="LedView"/> o <see cref="ButtonView"/>, las mismas vistas que en la HAT.
    /// </summary>
    /// <remarks>
    /// Cada colocación crea una vista nueva: al mover un componente se destruye la vieja. Así las
    /// suscripciones a los eventos del dominio (Observer) nunca se duplican.
    /// </remarks>
    public sealed class PlacedComponentView : MonoBehaviour
    {
        public DomainComponent Component { get; private set; }
        public Placement Placement { get; private set; }
        public LedView Led { get; private set; }
        public ButtonView Button { get; private set; }

        public static PlacedComponentView Create(Transform parent, Placement placement, BreadboardView board, SimulatorFacade facade)
        {
            var first = board.HoleToWorld(placement.Holes[0]);
            var last = board.HoleToWorld(placement.Holes[placement.Holes.Count - 1]);
            var go = new GameObject(placement.Component.Id);
            go.transform.SetParent(parent, false);
            go.transform.position = (first + last) / 2;
            go.transform.rotation = Quaternion.Euler(0, 0, ComponentArt.Angle(placement.Direction));

            var type = placement.Component.Type;
            float span = ComponentArt.Span(type);
            var parts = ComponentArt.Draw(go.transform, type, span, 10);

            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(span + 0.3f, 0.5f);

            var view = go.AddComponent<PlacedComponentView>();
            view.Component = placement.Component;
            view.Placement = placement;
            if (placement.Component is Led led)
            {
                view.Led = go.AddComponent<LedView>();
                view.Led.Bind(led, 0, parts.Body, parts.Glow, parts.BurntMark);
            }
            else if (placement.Component is PushButton button)
            {
                view.Button = go.AddComponent<ButtonView>();
                view.Button.Bind(facade, button, 0, parts.Cap);
            }
            return view;
        }
    }

    /// <summary>
    /// Vista de un cable: una línea (<see cref="LineRenderer"/>) que sigue el recorrido del A*
    /// por los agujeros o, si va a la HAT, una línea recta entre sus dos extremos.
    /// </summary>
    public sealed class WireView : MonoBehaviour
    {
        public const float Width = 0.09f;

        private static Material _material;
        private static readonly Color[] Palette =
        {
            new Color(0.20f, 0.55f, 0.95f), new Color(0.15f, 0.70f, 0.35f), new Color(0.95f, 0.75f, 0.15f),
            new Color(0.75f, 0.35f, 0.85f), new Color(0.95f, 0.45f, 0.20f), new Color(0.30f, 0.80f, 0.80f)
        };

        public Wire Wire { get; private set; }

        /// <summary>Puntos de la línea en el mundo.</summary>
        public IReadOnlyList<Vector3> Points { get; private set; }

        /// <summary>Material de las líneas (shader de sprites: color plano, sin luces).</summary>
        public static Material LineMaterial =>
            _material != null ? _material : (_material = new Material(Shader.Find("Sprites/Default")));

        public static WireView Create(Transform parent, Wire wire, IReadOnlyList<Vector3> points, int index)
        {
            var go = new GameObject("Cable " + wire.Id);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<WireView>();
            view.Wire = wire;
            view.Points = points;

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
            line.widthMultiplier = Width;
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            line.sharedMaterial = LineMaterial;
            var color = wire.Route.Count > 0 ? Palette[index % Palette.Length] : new Color(0.95f, 0.30f, 0.55f);
            line.startColor = line.endColor = color;
            line.sortingOrder = 6;

            // Colisionador a lo largo de la línea, para poder seleccionarlo (clic derecho = borrar)
            var edge = go.AddComponent<EdgeCollider2D>();
            var local = new Vector2[points.Count];
            for (int i = 0; i < points.Count; i++) local[i] = go.transform.InverseTransformPoint(points[i]);
            edge.points = local;
            edge.edgeRadius = 0.08f;
            return view;
        }
    }
}
