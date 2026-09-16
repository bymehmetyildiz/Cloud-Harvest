using UnityEngine;

public class Player : MonoBehaviour
{
    public StateMachine stateMachine;

    [HideInInspector]
    public Animator animator;

    //States
    public PlayerIdleState idleState;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        stateMachine = new StateMachine();
        idleState = new PlayerIdleState(stateMachine, "idle", GetComponent<CharacterController>(), this);
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
