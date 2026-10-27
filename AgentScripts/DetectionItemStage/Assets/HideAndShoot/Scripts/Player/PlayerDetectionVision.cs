using System;
using HideAndShoot.Combat;
using Unity.Netcode;
using UnityEngine;

namespace HideAndShoot.Player
{
    /// <summary>Server-owned seven-second buff. Only its owner receives the timer.</summary>
    public sealed class PlayerDetectionVision : NetworkBehaviour
    {
        public const double DurationSeconds=7d;
        public readonly NetworkVariable<double> DetectionEndsAt=new NetworkVariable<double>(
            0d,NetworkVariableReadPermission.Owner,NetworkVariableWritePermission.Server);
        private HostPlayerLife life;
        private PistolCombatController pistol;
        private PlayerLocalMotor motor;

        public double SecondsRemaining
        {
            get
            {
                if(!IsSpawned || (!IsOwner && !IsServer) || NetworkManager==null ||
                   life==null || life.IsDead.Value || pistol==null || !pistol.RoundActive.Value)return 0d;
                return Math.Max(0d,DetectionEndsAt.Value-NetworkManager.ServerTime.Time);
            }
        }
        public override void OnNetworkSpawn()
        {
            life=GetComponent<HostPlayerLife>();pistol=GetComponent<PistolCombatController>();
            motor=GetComponent<PlayerLocalMotor>();
            if(IsOwner)DetectionEndsAt.OnValueChanged+=OnTimerChanged;
            if(IsServer)
            {
                HostClear();
                life.IsDead.OnValueChanged+=OnDeathChanged;
                pistol.RoundId.OnValueChanged+=OnRoundChanged;
                pistol.RoundActive.OnValueChanged+=OnActiveChanged;
            }
        }
        public override void OnNetworkDespawn()
        {
            if(IsOwner)DetectionEndsAt.OnValueChanged-=OnTimerChanged;
            if(IsServer)
            {
                life.IsDead.OnValueChanged-=OnDeathChanged;
                pistol.RoundId.OnValueChanged-=OnRoundChanged;
                pistol.RoundActive.OnValueChanged-=OnActiveChanged;
                HostClear();
            }
        }
        public bool CanActivateForRound(int round)=>IsServer && IsSpawned && life!=null &&
            !life.IsDead.Value && life.RoundId==round && pistol!=null &&
            pistol.RoundActive.Value && pistol.RoundId.Value==round;
        public bool HostActivate(int round)
        {
            if(!CanActivateForRound(round))return false;
            DetectionEndsAt.Value=NetworkManager.ServerTime.Time+DurationSeconds;
            return true;
        }
        public void HostClear(){if(IsServer)DetectionEndsAt.Value=0d;}
        public bool CanSeePlayers(Camera camera)=>IsOwner && camera!=null &&
            camera.cameraType==CameraType.Game && motor!=null && camera==motor.PlayerCamera &&
            SecondsRemaining>0d;
        private void OnDeathChanged(bool previous,bool dead){if(dead)HostClear();}
        private void OnRoundChanged(int previous,int current)=>HostClear();
        private void OnActiveChanged(bool previous,bool active){if(!active)HostClear();}
        private void OnTimerChanged(double previous,double current)
        {
            if(current>previous && SecondsRemaining>0d)HideAndShoot.Audio.GameAudio.PlayUiClick();
        }
    }
}
