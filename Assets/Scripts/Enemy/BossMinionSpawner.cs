using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Sits on the boss alongside <see cref="FatRatBoss"/>. Spawns waves of
/// minion enemies during the fight based on HP thresholds AND/OR a recurring
/// interval. Keeps the boss feel dynamic by pulling the player's attention
/// </summary>
[RequireComponent(typeof(EntityStats))]
public class BossMinionSpawner : MonoBehaviour
{
    [Header("Minion Prefabs")]
    [Tooltip("Pool of enemy prefabs to spawn. The spawner picks one at random " +
             "from this list for each minion in a wave. Drag your grunt prefabs " +
             "here (GruntCone, GruntCircle, GruntRectangle, etc.).")]
    public GameObject[] minionPrefabs;

    [Header("HP Threshold Triggers")]
    [Tooltip("When the boss's HP fraction drops below any value in this list, " +
             "trigger a wave. Each threshold fires exactly once per fight.\n\n" +
             "Example: 0.75, 0.5, 0.25 means a wave at 75% HP, 50% HP, and 25% HP.")]
    [Range(0f, 1f)]
    public float[] hpThresholds = { 0.75f, 0.5f, 0.25f };

    [Tooltip("Minions per HP-threshold wave (legacy fallback — the per-threshold " +
             "array below wins when filled).")]
    public int hpThresholdWaveCount = 2;

    [Tooltip("Per-threshold spawn counts, parallel to HP Thresholds. " +
             "Design doc: 75% → 2, 50% → 3, 25% → 4.")]
    public int[] hpThresholdWaveCounts = { 2, 3, 4 };

    [Tooltip("Chasers spawned the FIRST time the boss takes any damage " +
             "(design doc: 1). 0 = off.")]
    public int firstHitSpawnCount = 1;

    [Header("Interval Spawning")]
    [Tooltip("If on, spawns a wave every spawnInterval seconds while the boss is alive.")]
    public bool enableIntervalSpawn = true;

    [Tooltip("Seconds between interval-based waves.")]
    public float spawnInterval = 20f;

    [Tooltip("If on, interval spawning only starts after the boss has been " +
             "hit at least once. Prevents minions appearing before combat begins.")]
    public bool requireDamageToStart = true;

    [Tooltip("Minions per interval wave.")]
    public int intervalWaveCount = 1;

    [Header("Spawn Geometry")]
    [Tooltip("Random angle around the boss is picked, then a distance between " +
             "these min/max values. Keeps minions from spawning ON the boss or " +
             "too far away.")]
    public float minSpawnDistance = 4f;

    [Tooltip("Max distance from the boss to spawn at.")]
    public float maxSpawnDistance = 10f;

    [Tooltip("When sampling the NavMesh for a valid spawn point, search within " +
             "this radius around the candidate point. Higher = more lenient about " +
             "uneven terrain.")]
    public float navmeshSampleRadius = 2f;

    [Tooltip("Number of retry attempts per minion if the first random position " +
             "isn't on the NavMesh. Prevents infinite loops on closed arenas.")]
    public int spawnAttempts = 6;

    [Header("Limits")]
    [Tooltip("Maximum minions alive at the same time. Prevents the arena from " +
             "getting flooded if the player is slow at clearing.")]
    public int maxAliveMinions = 5;

    [Header("Spawned Minion Behavior")]
    [Tooltip("If on, every spawned minion has its EnemyAI.permanentlyAggroed " +
             "flag set to true immediately. They ignore aggroRange and hunt the " +
             "player from anywhere in the arena. Recommended for boss fights — " +
             "minions are meant to pressure the player, not patrol passively.")]
    public bool forceMinionAggro = true;

    [Tooltip("Move-speed multiplier applied to every spawned minion. 1.0 = " +
             "normal grunt speed (from the stat block). 1.5 = 50% faster than " +
             "the prefab default. Higher values make minions more threatening " +
             "in the boss arena.\n\n" +
             "Sets the EnemyAI.speedMultiplier field on each spawn before its " +
             "Start runs, so the NavMeshAgent picks up the new value immediately.")]
    [Range(0.5f, 3f)]
    public float minionSpeedMultiplier = 1.5f;

    [Header("Debug")]
    public bool verbose = false;
    public bool showGizmos = true;

    // ── Runtime ──
    private EntityStats        _stats;
    private List<EntityStats>  _aliveMinions = new List<EntityStats>();
    private HashSet<float>     _thresholdsTriggered = new HashSet<float>();
    private float              _nextIntervalSpawn;
    private bool               _damageTaken;
    private bool               _bossAlive = true;


    void Awake()
    {
        _stats = GetComponent<EntityStats>();
    }

    void Start()
    {
        _stats.onDamageTaken.AddListener(OnBossDamaged);
        _stats.onDeath.AddListener(OnBossDied);

        _nextIntervalSpawn = Time.time + spawnInterval;
    }

    void OnDestroy()
    {
        if (_stats != null)
        {
            _stats.onDamageTaken.RemoveListener(OnBossDamaged);
            _stats.onDeath.RemoveListener(OnBossDied);
        }
    }

    void Update()
    {
        if (!_bossAlive) return;
        if (!enableIntervalSpawn) return;
        if (requireDamageToStart && !_damageTaken) return;
        if (Time.time < _nextIntervalSpawn) return;

        SpawnWave(intervalWaveCount);
        _nextIntervalSpawn = Time.time + spawnInterval;
    }

    // ── Event handlers ──

    private void OnBossDamaged(int _)
    {
        // First hit taken → the opening chaser (design doc: 1).
        if (!_damageTaken)
        {
            _damageTaken = true;
            if (firstHitSpawnCount > 0)
            {
                if (verbose)
                    Debug.Log($"[BossMinionSpawner] First hit — spawning {firstHitSpawnCount} chaser(s).");
                SpawnWave(firstHitSpawnCount);
            }
        }

        if (_stats.MaxHealth <= 0) return;
        float hpFraction = (float)_stats.CurrentHealth / _stats.MaxHealth;

        // Check each threshold — fire the wave if we just crossed below it.
        // Per-threshold counts (doc: 75% → 2, 50% → 3, 25% → 4) come from
        // hpThresholdWaveCounts; falls back to the single legacy count.
        for (int i = 0; i < hpThresholds.Length; i++)
        {
            float threshold = hpThresholds[i];
            if (_thresholdsTriggered.Contains(threshold)) continue;
            if (hpFraction <= threshold)
            {
                _thresholdsTriggered.Add(threshold);

                int count = (hpThresholdWaveCounts != null && i < hpThresholdWaveCounts.Length)
                    ? hpThresholdWaveCounts[i]
                    : hpThresholdWaveCount;

                if (verbose)
                    Debug.Log($"[BossMinionSpawner] HP threshold {threshold:P0} " +
                              $"crossed (now {hpFraction:P0}) — spawning {count}.");

                SpawnWave(count);
            }
        }
    }

    private void OnBossDied()
    {
        _bossAlive = false;
        if (verbose) Debug.Log("[BossMinionSpawner] Boss died — spawner stopped.");
    }

    // ── Spawn logic ──

    /// <summary>
    /// Manually trigger a wave. Public so external scripts can fire it too.
    /// </summary>
    public void SpawnWave(int count)
    {
        if (minionPrefabs == null || minionPrefabs.Length == 0)
        {
            Debug.LogWarning("[BossMinionSpawner] No minion prefabs assigned — skipping wave.");
            return;
        }

        PruneDeadMinions();

        int actuallySpawned = 0;
        for (int i = 0; i < count; i++)
        {
            if (_aliveMinions.Count >= maxAliveMinions)
            {
                if (verbose) Debug.Log("[BossMinionSpawner] Max alive minions reached — wave cut short.");
                break;
            }

            if (TrySpawnOne()) actuallySpawned++;
        }

        if (verbose)
            Debug.Log($"[BossMinionSpawner] Spawned {actuallySpawned}/{count} minions " +
                      $"({_aliveMinions.Count} alive total).");
    }

    private bool TrySpawnOne()
    {
        GameObject prefab = minionPrefabs[Random.Range(0, minionPrefabs.Length)];
        if (prefab == null) return false;

        // Try a few random points around the boss until we find one on the NavMesh.
        for (int attempt = 0; attempt < spawnAttempts; attempt++)
        {
            Vector2 ringPoint = Random.insideUnitCircle.normalized
                              * Random.Range(minSpawnDistance, maxSpawnDistance);
            Vector3 candidate = transform.position + new Vector3(ringPoint.x, 0f, ringPoint.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit,
                                       navmeshSampleRadius, NavMesh.AllAreas))
            {
                GameObject minion = Instantiate(prefab, hit.position,
                                                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

                // Apply spawn-time tweaks (aggro + speed) to the new minion's
                // EnemyAI. These get set BEFORE the minion's Start runs, so when
                // Start fires next frame it picks up the new values.
                EnemyAI ai = minion.GetComponent<EnemyAI>();
                if (ai != null)
                {
                    if (forceMinionAggro)      ai.permanentlyAggroed = true;
                    if (minionSpeedMultiplier > 0f) ai.speedMultiplier = minionSpeedMultiplier;
                }

                var stats = minion.GetComponent<EntityStats>();
                if (stats != null)
                {
                    _aliveMinions.Add(stats);
                    // When this minion dies, free up its slot.
                    stats.onDeath.AddListener(() => OnMinionDied(stats));
                }
                return true;
            }
        }

        if (verbose)
            Debug.LogWarning("[BossMinionSpawner] No valid NavMesh point found in " +
                             $"{spawnAttempts} attempts — skipping this minion.");
        return false;
    }

    private void OnMinionDied(EntityStats minion)
    {
        if (minion != null) _aliveMinions.Remove(minion);
    }

    private void PruneDeadMinions()
    {
        // Clean up any destroyed/null references from the alive list.
        for (int i = _aliveMinions.Count - 1; i >= 0; i--)
        {
            if (_aliveMinions[i] == null || _aliveMinions[i].IsDead)
                _aliveMinions.RemoveAt(i);
        }
    }

    // ── Gizmos ──

    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        // Inner ring — minimum spawn distance
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
        DrawCircleGizmo(transform.position, minSpawnDistance, 32);

        // Outer ring — maximum spawn distance
        Gizmos.color = new Color(1f, 0.2f, 0f, 0.6f);
        DrawCircleGizmo(transform.position, maxSpawnDistance, 48);
    }

    private static void DrawCircleGizmo(Vector3 origin, float radius, int segments)
    {
        Vector3 prev = origin + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            Vector3 cur = origin + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }
    }
}
