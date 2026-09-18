using UnityEngine;

public class PlayerState
{
    protected StateMachine stateMachine;
    protected Player player;
    protected string animBoolName;
    protected CharacterController controller;

    protected float xInput;


    protected float stateTimer;
    protected bool triggerCalled;

    public PlayerState(StateMachine stateMachine, string animBoolName, CharacterController controller, Player player)
    {
        this.stateMachine = stateMachine;
        this.animBoolName = animBoolName;
        this.controller = controller;
        this.player = player;
    }

    public virtual void Enter()
    {
        player.animator.SetBool(animBoolName, true);
        triggerCalled = false;
        stateTimer = 0f;
    }

    public virtual void Update()
    {
        stateTimer -= Time.deltaTime;
    }


    public virtual void Exit()
    {
        player.animator.SetBool(animBoolName, false);
    }

    public virtual void TriggerAnimation() => triggerCalled = true;


    public virtual void UnTriggerAnimation() => triggerCalled = false;

}