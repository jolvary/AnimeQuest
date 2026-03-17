using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    private async void Start()
    {
        try
        {
            await NakamaAuthManager.Instance.LoginDeviceAsync();
            string me = await ApiClient.Instance.PostEnsureMe();
            Debug.Log("Authenticated and ensured user: " + me);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Bootstrap failed: " + ex.Message);
        }
    }
}