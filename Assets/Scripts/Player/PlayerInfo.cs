using PlayFab;
using PlayFab.ClientModels;
using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public class PlayerInfo : MonoBehaviourPun, IPunObservable
{
    [Header("PlayerAttributes")]
    [SerializeField] public const int maxHealth = 100;
    [SerializeField] private int _currentHealth;
    [SerializeField] private int _currentAmmo;
    [SerializeField] private int _timeSurvived = 0;
    [SerializeField] private int _kills = 0;
    [SerializeField] private bool _isDown = false;
    [SerializeField] private bool _invincible = false;
    public bool gettingUp = false;
    [SerializeField] private float getUpTime = 5f; 
    [SerializeField] private PlayerUI playerUI = null;
    [SerializeField] private PlayerAim playerAim;

    public bool IsDown { get => _isDown; }

    void Awake() {
        _currentHealth = maxHealth;
    }

    void Start() {
        StartCoroutine(timeCounter());
        if (photonView.IsMine) {
            playerUI = playerAim.playerUI;
            ResetStatistics();
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(_currentHealth);
            stream.SendNext(_currentAmmo);
            stream.SendNext(_invincible);
            stream.SendNext(_isDown);
        } else
        {
            _currentHealth = (int)stream.ReceiveNext();
            _currentAmmo = (int)stream.ReceiveNext();
            _invincible = (bool)stream.ReceiveNext();
            _isDown = (bool)stream.ReceiveNext();
        }
    }

    void Update()
    {
        if (photonView.IsMine)
        {
            playerUI.SetHealth(_currentHealth);
            if (Input.GetKeyDown(KeyCode.Q) && _currentHealth < maxHealth) {
                InventoryManager.Instance.UseItem(InventoryManager.Item.MK, 1, () => {
                    HealPlayer(60);
                    ResetInventory();
                });
            }
            if (Input.GetKeyDown(KeyCode.E)) {
                FinishGame();
            }
        }

        if (gettingUp) {
            getUpTime -= Time.deltaTime;
            if (getUpTime <= 0) {
                GetUp();
            }
        } else {
            getUpTime = 5;
        }
    }

    public void HealPlayer(int amount) {
        _currentHealth += amount;
        if (_currentHealth > maxHealth) {
            _currentHealth = maxHealth;
            if (playerUI != null) {
                playerUI.SetHealth(_currentHealth);
            }
        }
    }

    public void DamagePlayer(int amount) {
        if (!_invincible) {
            _currentHealth -= amount;
            photonView.RPC(nameof(DamagePlayerPun), RpcTarget.All);
            if (_currentHealth <= 0) {
                DownPlayer();
            }
        }
    }

    public void GetUp() {
        _isDown = false;
    }

    public void IncreaseKills() {
        _kills += 1;
        photonView.RPC(nameof(EnemyKill), RpcTarget.All);
    }

    IEnumerator timeCounter() {
        while (true) {
            yield return new WaitForSecondsRealtime(1);
            _timeSurvived += 1;
        }
    }

    IEnumerator Invincible() {
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        sprite.color = new Vector4(sprite.color.r, sprite.color.g, sprite.color.b, 0.4f);
        _invincible = true;
        yield return new WaitForSeconds(1);
        sprite.color = new Vector4(sprite.color.r, sprite.color.g, sprite.color.b, 1f);
        _invincible = false;
    }

    public void CollectCoin(int amount) {
        CurrencyManager.Instance.AddCurrency(CurrencyManager.VirtualCurrency.CO, amount);
        ResetCurrency();
    }

    public void DownPlayer() {
        _isDown = true;
    }



    public void ResetStatistics() {
        ResetInventory();
        ResetCurrency();
    }

    public void ResetInventory() {
        var request = new GetPlayerCombinedInfoRequest();
        request.InfoRequestParameters = new GetPlayerCombinedInfoRequestParams();
        request.InfoRequestParameters.GetUserInventory = true;
        PlayFabClientAPI.GetPlayerCombinedInfo(
            request, (result) => {
                if (result.InfoResultPayload.UserInventory != null)
                {
                    foreach (var item in result.InfoResultPayload.UserInventory)
                    {
                        switch(item.ItemId) {
                            case "MK":
                                playerUI.SetMedkitText(item.RemainingUses.ToString());
                                break;
                            case "AM":
                                playerUI.SetAmmo(playerAim.MagazineCapacity, (int)item.RemainingUses);
                                playerAim.AmmoCapacity = (int)item.RemainingUses;
                            break;
                        }
                    }
                } else {
                    Debug.Log("No player inventory found");
                }
            }, (fail) => {
                Debug.Log("No Player Info found");
            }
        );
    }

    public void ResetCurrency() {
        var request = new GetPlayerCombinedInfoRequest();
        request.InfoRequestParameters = new GetPlayerCombinedInfoRequestParams();
        request.InfoRequestParameters.GetUserVirtualCurrency = true;
        PlayFabClientAPI.GetPlayerCombinedInfo(
            request, (result) => {
                foreach (var item in result.InfoResultPayload.UserVirtualCurrency)
                {
                    switch(item.Key) {
                        case "CO":
                            playerUI.SetCoinText(item.Value.ToString());
                        break;
                    }
                }
            }, (fail) => {
                Debug.Log("No Player Info found");
            }
        );
    }

    [PunRPC]
    public void DamagePlayerPun() {
        StartCoroutine(Invincible());
    }

    [PunRPC]
    public void EnemyKill() {
        EnemySpawner.Instance.ReduceEnemyCount();
    }

    public void FinishGame() {
        PlayFabClientAPI.UpdatePlayerStatistics( new UpdatePlayerStatisticsRequest()
        {
            Statistics  = new List<StatisticUpdate>
            { new StatisticUpdate() 
                {
                    StatisticName = "Time", 
                    Value = _timeSurvived
                },
                new StatisticUpdate() {
                    StatisticName = "Kills", 
                    Value = _kills
                }
            }
        }, (updateSuccess) =>
        {
            Debug.Log("Wins Statistic Update Success");
        }, (updateFailure) => { 
            Debug.Log("Wins Statistic Update failed");
        }); 
    }

    private void WinCondition()
    {
        PlayFabClientAPI.UpdatePlayerStatistics( new UpdatePlayerStatisticsRequest()
        {
            Statistics  = new List<StatisticUpdate>
            { new StatisticUpdate() 
                {
                    StatisticName = "Wins", 
                    Value = 1
                }
            }
        }, (updateSuccess) =>
        {
            Debug.Log("Wins Statistic Update Success");
        }, (updateFailure) => { 
            Debug.Log("Wins Statistic Update Failed");
        });   
    }

}
