using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace BaseLib.Abstracts;

public abstract class CustomPetModel(bool visibleHp) : CustomMonsterModel {
    public override bool IsHealthBarVisible => visibleHp;
    
    //Visuals are defined the same way as CustomMonsterModel.

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState nothingState = new MoveState("NOTHING_MOVE",_ => Task.CompletedTask);
        nothingState.FollowUpState = nothingState;
        return new MonsterMoveStateMachine([nothingState], nothingState);
    }
}