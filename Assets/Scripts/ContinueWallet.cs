using UnityEngine;

/// <summary>
/// Persistent continue credits shared by every standard game mode.
/// </summary>
public static class ContinueWallet
{
    public const int Capacity = 1;
    public const int InitialBalance = Capacity;
    public const string StorageKey = "Continues.Remaining";

    public static int Remaining
    {
        get
        {
            if (!PlayerPrefs.HasKey(StorageKey))
            {
                Store(InitialBalance);
                return InitialBalance;
            }

            int stored = PlayerPrefs.GetInt(StorageKey);
            if (stored > Capacity)
            {
                Debug.Log(
                    $"[ContinueWallet] Capping legacy balance {stored} at {Capacity}.");
                Store(Capacity);
                return Capacity;
            }

            if (stored >= 0) return stored;

            Debug.LogWarning(
                $"[ContinueWallet] Correcting invalid stored balance {stored}.");
            Store(0);
            return 0;
        }
    }

    public static bool TrySpend(out int remaining)
    {
        int current = Remaining;
        if (current <= 0)
        {
            remaining = 0;
            return false;
        }

        remaining = current - 1;
        Store(remaining);
        return true;
    }

    public static int GrantRewardedContinues()
    {
        Store(Capacity);
        return Capacity;
    }

    public static int RefundOne()
    {
        int updated = Mathf.Min(Capacity, Remaining + 1);
        Store(updated);
        return updated;
    }

    private static void Store(int balance)
    {
        PlayerPrefs.SetInt(StorageKey, Mathf.Clamp(balance, 0, Capacity));
        PlayerPrefs.Save();
    }
}
