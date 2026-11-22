namespace NovaTaskbar.Core.Magnification;

public enum EasingCurve
{
    Linear,
    EaseOutCubic,
    EaseInOutSine,
    EaseOutQuint,
    /// <summary>Mola criticamente amortecida (padrão): interrompível, parte do valor e da velocidade atuais.</summary>
    Spring,
}

public static class Easing
{
    public static double Apply(EasingCurve curve, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return curve switch
        {
            EasingCurve.Linear => t,
            EasingCurve.EaseOutCubic => 1.0 - Math.Pow(1.0 - t, 3),
            EasingCurve.EaseInOutSine => -(Math.Cos(Math.PI * t) - 1.0) / 2.0,
            EasingCurve.EaseOutQuint => 1.0 - Math.Pow(1.0 - t, 5),
            // Mola não é função do tempo normalizado; como curva estática equivale a um ease-out forte.
            EasingCurve.Spring => 1.0 - Math.Pow(1.0 - t, 4),
            _ => t,
        };
    }

    /// <summary>cubic-bezier(0.23, 1, 0.32, 1): ease-out forte para entradas/saídas de UI.</summary>
    public static double StrongEaseOut(double t) => CubicBezier(0.23, 1, 0.32, 1, Math.Clamp(t, 0, 1));

    /// <summary>Avalia uma curva cubic-bezier CSS (x1,y1,x2,y2) em t∈[0,1] por Newton-Raphson + bisseção.</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx;
        double cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
        double SampleX(double t) => ((ax * t + bx) * t + cx) * t;
        double SampleY(double t) => ((ay * t + by) * t + cy) * t;
        double SlopeX(double t) => (3 * ax * t + 2 * bx) * t + cx;

        double u = x;
        for (int i = 0; i < 8; i++)
        {
            double err = SampleX(u) - x;
            if (Math.Abs(err) < 1e-6) return SampleY(u);
            double d = SlopeX(u);
            if (Math.Abs(d) < 1e-6) break;
            u -= err / d;
        }
        double lo = 0, hi = 1;
        u = x;
        for (int i = 0; i < 40; i++)
        {
            double v = SampleX(u);
            if (Math.Abs(v - x) < 1e-6) break;
            if (v < x) lo = u; else hi = u;
            u = (lo + hi) / 2;
        }
        return SampleY(u);
    }
}

/// <summary>
/// Mola amortecida no modelo da Apple: <see cref="Response"/> (s, rapidez) e <see cref="Damping"/>
/// (1 = crítica, sem overshoot; &lt; 1 = quica). Interrompível: mudar o alvo preserva valor e velocidade.
/// </summary>
public struct Spring
{
    public double Value;
    public double Velocity;
    public double Target;
    public double Response;
    public double Damping;
    /// <summary>Distância/velocidade abaixo da qual a mola é considerada parada.</summary>
    public double Epsilon;

    public Spring(double value, double response, double damping = 1.0, double epsilon = 0.001)
    {
        Value = Target = value;
        Velocity = 0;
        Response = response;
        Damping = damping;
        Epsilon = epsilon;
    }

    public readonly bool IsSettled => Math.Abs(Target - Value) <= Epsilon && Math.Abs(Velocity) <= Epsilon * 10;

    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    /// <summary>Avança <paramref name="dt"/> segundos (integração semi-implícita em subpassos de ≤ 1/240 s).</summary>
    public void Step(double dt)
    {
        if (dt <= 0) return;
        if (Response <= 0) { Snap(Target); return; }
        double omega = 2 * Math.PI / Response;
        double k = omega * omega;
        double c = 2 * Math.Max(0, Damping) * omega;
        int n = Math.Max(1, (int)Math.Ceiling(dt * 240));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            double a = -k * (Value - Target) - c * Velocity;
            Velocity += a * h;
            Value += Velocity * h;
        }
        if (IsSettled) Snap(Target);
    }
}

/// <summary>
/// Estado temporal da magnificação: intensidade (entrada/saída do cursor) e foco suavizado.
/// Independente de UI; avançado por <see cref="Advance"/> a cada quadro enquanto houver animação.
/// </summary>
public sealed class MagnificationAnimator
{
    private double _progress;          // 0..1 linear (curvas por duração)
    private bool _hovering;
    private double _targetFocus;
    private Spring _intensity = new(0, 0.18, 1.0, 0.0005);
    private Spring _focus = new(0, 0.14, 1.0, 0.05);

    public double Focus { get; private set; }
    public double Intensity { get; private set; }

    /// <summary>Duração (curvas) ou resposta da mola (Spring) da entrada/saída, em segundos.</summary>
    public double Duration { get; set; } = 0.18;

    /// <summary>Constante de tempo do acompanhamento do cursor, em segundos (0 = imediato).</summary>
    public double FollowTimeConstant { get; set; } = 0.035;

    public EasingCurve Curve { get; set; } = EasingCurve.EaseOutCubic;

    public bool IsHovering => _hovering;

    private bool UsesSpring => Curve == EasingCurve.Spring;

    /// <summary>Verdadeiro enquanto ainda há movimento a renderizar.</summary>
    public bool IsAnimating => UsesSpring
        ? !_intensity.IsSettled || (_hovering && !_focus.IsSettled)
        : (_hovering && _progress < 1.0) || (!_hovering && _progress > 0.0) || (_hovering && Math.Abs(_targetFocus - Focus) > 0.05);

    public void PointerMoved(double focus)
    {
        bool fromRest = !_hovering && Intensity <= 0.0005;
        if (fromRest)
        {
            Focus = focus; // entrada a partir do repouso: sem salto, a intensidade ainda é 0
            _focus.Snap(focus);
        }
        _hovering = true;
        _targetFocus = focus;
        _focus.Target = focus;
        _intensity.Target = 1;
    }

    public void PointerExited()
    {
        _hovering = false;
        _intensity.Target = 0;
    }

    /// <summary>Avança o estado em <paramref name="dt"/> segundos. Retorna <see cref="IsAnimating"/>.</summary>
    public bool Advance(double dt)
    {
        if (dt < 0) dt = 0;
        if (dt > 0.1) dt = 0.1; // evita saltos após pausas longas do UI thread

        if (UsesSpring)
        {
            _intensity.Response = Math.Max(0, Duration);
            _focus.Response = Math.Max(0, FollowTimeConstant * 4);
            _intensity.Step(dt);
            if (_hovering) _focus.Step(dt);
            Intensity = Math.Clamp(_intensity.Value, 0, 1);
            Focus = _focus.Value;
            _progress = Intensity;
            return IsAnimating;
        }

        double step = Duration <= 0 ? 1.0 : dt / Duration;
        _progress = _hovering ? Math.Min(1.0, _progress + step) : Math.Max(0.0, _progress - step);
        Intensity = Easing.Apply(Curve, _progress);
        _intensity.Snap(Intensity);

        if (_hovering)
        {
            if (FollowTimeConstant <= 0) Focus = _targetFocus;
            else
            {
                double a = 1.0 - Math.Exp(-dt / FollowTimeConstant);
                Focus += (_targetFocus - Focus) * a;
            }
            _focus.Snap(Focus);
        }

        return IsAnimating;
    }

    /// <summary>Volta imediatamente ao repouso.</summary>
    public void Reset()
    {
        _hovering = false;
        _progress = 0;
        Intensity = 0;
        _intensity.Snap(0);
    }
}
