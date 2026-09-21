using JetBrains.Annotations;
using UnityEngine;

public class Player : MonoBehaviour
{
    public StateMachine stateMachine;

    [HideInInspector]
    public Animator animator;
    public CharacterController controller;
    private InputSystem_Actions controls;
    public Vector2 moveInput;

    //Vairables
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float turnSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;
    private float verticalVelocity;


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
        
        stateMachine = new StateMachine();
        controls = new InputSystem_Actions();
        animator = GetComponentInChildren<Animator>();
        controller = GetComponent<CharacterController>();

        controls.Player.Move.performed += context => moveInput = context.ReadValue<Vector2>();
        controls.Player.Move.canceled += context => moveInput = Vector2.zero;


        //States
        idleState = new PlayerIdleState(stateMachine, "Idle", controller, this);
        moveState = new PlayerMoveState(stateMachine, "Move", controller, this);
        interactState = new PlayerInteractState(stateMachine, "Interact", controller, this);
        chopState = new PlayerChopState(stateMachine, "Chop", controller, this);
        digState = new PlayerDigState(stateMachine, "Dig", controller, this);
        fishingState = new PlayerFishingState(stateMachine, "Fishing", controller, this);
        hammeringState = new PlayerHammeringState(stateMachine, "Hammering", controller, this);
        holdingState = new PlayerHoldingState(stateMachine, "Holding", controller, this);
        lockPickState = new PlayerLockPickState(stateMachine, "Lockpick", controller, this);
        pixaxeState = new PlayerPixaxeState(stateMachine, "Pixaxe", controller, this);
        workState = new PlayerWorkState(stateMachine, "Work", controller, this);
    }

    void Start()
    {
        

        stateMachine.Initialize(idleState);
    }

    
    void Update()
    {
        stateMachine.currentState.Update();

        ApplyGravity();
    }

    private void OnEnable()
    {
        controls.Enable();
    }

    private void OnDisable()
    {
        controls.Disable();
    }

    private void ApplyGravity()
    {
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        else
            verticalVelocity += gravity * Time.deltaTime;

        controller.Move(
            Vector3.up * verticalVelocity * Time.deltaTime
        );
    }

    public void ApplyMovement()
    {
        Vector3 move = new Vector3(
            moveInput.x,
            0f,
            moveInput.y
        );

        if (move.sqrMagnitude > 1f)
            move.Normalize();

        Quaternion targetRotation = Quaternion.LookRotation(move);
        move.Normalize();

        if(IsMoving())
            transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            turnSpeed * Time.deltaTime
        );

        controller.Move(move * moveSpeed * Time.deltaTime);
    }

    public bool IsMoving()
    {
        return moveInput.sqrMagnitude > 0.01f;
    }



}



