namespace PeopleCore.Domain.Enums;

public enum SeparationType { Resignation, TerminationJustCause, AuthorizedCause, EndOfContract, Retirement, Death }

/// <summary>Labor Code Art. 298-299 causes; required when the type is AuthorizedCause.</summary>
public enum AuthorizedCause { Redundancy, Retrenchment, ClosureNotDueToLosses, ClosureDueToSeriousLosses, LaborSavingDevices, Disease }

public enum SeparationStatus { NoticeGiven, Separated }
