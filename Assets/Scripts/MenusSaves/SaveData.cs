using System;

[Serializable]
public class SaveData
{
    public bool   hasData       = false;
    public string saveName      = "";      // player-entered name e.g. "Run 1"
    public string saveDate      = "";
    public float  totalPlayTime = 0f;

    public string currentSceneName = "";
    public int    currentFloor     = 1;

    public int currentLevel   = 0;
    public int currentXP      = 0;
    public int unspentPoints  = 0;

    public int maxHealth      = 100;
    public int currentHealth  = 100;
    public int strength       = 5;
    public int maxStamina     = 50;
    public int currentStamina = 50;
    public int speed          = 5;

    public int equippedWeapon = 0;
}