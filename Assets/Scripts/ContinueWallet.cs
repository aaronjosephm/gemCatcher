using UnityEngine;

/// <summary>
/// Persistent continue credits shared by every standard game mode.
/// </summary>
public static class ContinueWallet
{
    public const int InitialBalance = 3;
    public const int RewardedGrant = 3;
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
        long updated = (long)Remaining + RewardedGrant;
        int persisted = updated > int.MaxValue ? int.MaxValue : (int)updated;
        Store(persisted);
        return persisted;
    }

    public static int RefundOne()
    {
        int current = Remaining;
        int updated = current == int.MaxValue ? current : current + 1;
        Store(updated);
        return updated;
    }

    private static void Store(int balance)
    {
        PlayerPrefs.SetInt(StorageKey, Mathf.Max(0, balance));
        PlayerPrefs.Save();
    }
}
