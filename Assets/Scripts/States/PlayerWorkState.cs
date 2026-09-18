using UnityEngine;

public class PlayerWorkState : PlayerState
{
    public PlayerWorkState(StateMachine stateMachine, string animBoolName, CharacterController controller, Player player) : base(stateMachine, animBoolName, controller, player)
    {
    }

    public override void Enter()
    {
        base.Enter();
    }

    public override void Exit()
    {
        base.Exit();
    }

    public override void Update()
    {
        base.Update();
    }
}
