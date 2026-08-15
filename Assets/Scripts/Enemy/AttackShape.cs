// AttackShape.cs
// Place anywhere in your project (e.g. Scripts/Combat/AttackShape.cs).
// No MonoBehaviour needed — this is just the shared enum.

/// <summary>
/// Shared enum for enemy attack telegraph shapes.
/// Used by EnemyCombat (hit check + indicator), CaptainCombat (shape cycling),
/// and EnemyStatBlock (per-enemy shape + dimensions).
/// </summary>
public enum AttackShape
{
    Cone,
    Circle,
    Rectangle
}