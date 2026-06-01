using UnityEngine;

public class DoorBlock : MonoBehaviour
{
    [SerializeField] private float moveDistance = 5f;
    [SerializeField] private float moveSpeed = 2f;

    private Vector3 _targetPosition;
    private bool _shouldMove = false;

    private void Start()
    {
        _targetPosition = transform.position;
    }

    private void Update()
    {
        if (!_shouldMove && AreAllEnemiesDead())
        {
            _shouldMove = true;
            _targetPosition = new Vector3(transform.position.x, transform.position.y - moveDistance, transform.position.z);
            Debug.Log($"All enemies dead! Moving door to {_targetPosition}");
        }

        if (_shouldMove)
        {
            transform.position = Vector3.MoveTowards(transform.position, _targetPosition, moveSpeed * Time.deltaTime);
        }
    }

    private bool AreAllEnemiesDead()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer == -1)
        {
            Debug.LogError("Enemy layer not found! Make sure the layer is named exactly 'Enemy'");
            return false;
        }

        foreach (var col in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (col.gameObject.layer == enemyLayer)
                return false;
        }
        return true;
    }
}