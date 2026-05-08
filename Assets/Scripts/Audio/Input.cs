using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private void Update()
    {
        if(Input.GetKeyDown(KeyCode.Mouse0))
        {
            AudioManager.Instance.Play(AudioManager.SoundType.Attack);
        }
    } 
    
}