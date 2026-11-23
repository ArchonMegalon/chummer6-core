namespace Chummer.Contracts.Session;

// Review data only. Confirmation re-resolves the current owner, character,
// source and journal through the existing operation transaction.
public sealed record SessionPlayAmmunitionReview5701(SessionPatchRequest Request, string PayloadDigest,
    SessionPlayAmmunition Before, SessionPlayAmmunition After);
