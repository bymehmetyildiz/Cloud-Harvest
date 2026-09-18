using UnityEngine;

public class Player : MonoBehaviour
{
    public StateMachine stateMachine;

    [HideInInspector]
    public Animator animator;
    public CharacterController controller;

    //States
    public PlayerIdleState idleState;
    public PlayerMoveState moveState;
    public PlayerInteractState interactState;
    public PlayerChopState chopState;
    public PlayerDigState digState;
    public PlayerFishingState fishingState;
    public PlayerHammeringState hammeringState;
    public PlayerHoldingState holdingState;
    public PlayerLockPickState lockPickState;
    public PlayerPixaxeState pixaxeState;
    public PlayerWorkState workState;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        stateMachine = new StateMachine();

        //States
        idleState = new PlayerIdleState(stateMachine, "idle", controller, this);
        moveState = new PlayerMoveState(stateMachine, "move", controller, this);
        interactState = new PlayerInteractState(stateMachine, "interact", controller, this);
        chopState = new PlayerChopState(stateMachine, "chop", controller, this);
        digState = new PlayerDigState(stateMachine, "dig", controller, this);
        fishingState = new PlayerFishingState(stateMachine, "fishing", controller, this);
        hammeringState = new PlayerHammeringState(stateMachine, "hammering", controller, this);
        holdingState = new PlayerHoldingState(stateMachine, "holding", controller, this);
        lockPickState = new PlayerLockPickState(stateMachine, "lockPick", controller, this);
        pixaxeState = new PlayerPixaxeState(stateMachine, "pixaxe", controller, this);
        workState = new PlayerWorkState(stateMachine, "work", controller, this);
    }

    void Start()
    {
        stateMachine.Initialize(idleState);
    }

    
    void Update()
    {
        stateMachine.currentState.Update();
    }
}
