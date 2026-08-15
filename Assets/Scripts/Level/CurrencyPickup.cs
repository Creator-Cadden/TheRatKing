using UnityEngine;

/// <summary>
/// A collectable that grants currency when the player touches it. Drop it in a
/// scene (or spawn it from a dead enemy) and set the amount. Uses a trigger
/// collider; optional spin + bob for a bit of juice.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CurrencyPickup : MonoBehaviour
{
    [Header("Value")]
    [Tooltip("Currency granted when the player picks this up.")]
    public int amount = 5;

    [Header("Juice (optional)")]
    public bool  spin      = true;
    public float spinSpeed = 90f;   // degrees/sec
    public float bobHeight = 0.15f; // world units
    public float bobSpeed  = 2f;

    private Vector3 _startPos;
    private bool    _collected;

    void Awake()
    {
        _startPos = transform.position;
        // Pickups must be triggers so the player passes through and collects.
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    void Update()
    {
        if (spin)
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        if (bobHeight > 0f)
            transform.position = _startPos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_collected) return;

        // Parent search so a child collider on the player still works.
        CurrencySystem wallet = other.GetComponentInParent<CurrencySystem>();
        if (wallet == null) return;

        _collected = true;
        wallet.AddCurrency(amount, "Pickup");
        Destroy(gameObject);
    }
}
