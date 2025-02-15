using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ThinAerofoilComponent : AerodynamicComponent
{
    // Thin Aero Foil aerodynamics uses a moving centre of pressure
    // and a normal force model to determine the lift and moment due to lift
    // Only induced drag is added by this component!


    public float aspectRatioCorrection_kAR, thicknessCorrection_kt; // (dimensionless)
    const float thicknessCorrection_labdat = 6f;                    // (dimensionless)
    // Blend between pre and post stall
    public float stallAngle, alpha_0;           // (rad)
    public float upperSigmoid, lowerSigmoid;    // (dimensionless)
    public float preStallFilter;                // (dimensionless)
    public float stallAngleMin = 15f;           // (deg)
    public float stallAngleMax = 35f;           // (deg)
    public float stallSharpness = 0.75f;        // (dimensionless)

    // Lift
    public float CL;                            // (dimensionless)
    public float CZmax = 1.2f;                  // (dimensionless)
    public float liftCurveSlope;                // (dimensionless)
    public float CL_preStall, CL_postStall;     // (dimensionless)
    public float CD_induced;                    // (dimensionless)

    // Moment due to lift and camber
    public float CM;                            // (dimensionless)
    public float CM_0, CM_delta;                // (dimensionless)
    public float CoP_z;                         // (m)

    public override void RunModel(AeroBody aeroBody)
    {
        // Prandtyl Theory
        // Clamp lower value to min AR of 2. Otherwise lift curve slope gets lower than sin 2 alpha which is non physical
        // Check for the divide by zero, although I think a zero AR means bigger problems anyway
        aspectRatioCorrection_kAR = aeroBody.EAB.aspectRatio == 0f ? 0f : Mathf.Clamp(aeroBody.EAB.aspectRatio / (2f + aeroBody.EAB.aspectRatio), 0f, 1f);

        // This value needs checking for thickness to chord ratio of 1

        // Empirical correction to account for viscous effects across all thickness to chord ratios
        thicknessCorrection_kt = Mathf.Exp(-thicknessCorrection_labdat * aeroBody.EAB.thicknessToChordRatio_bOverc * aeroBody.EAB.thicknessToChordRatio_bOverc);

        // Emperical relation to allow for viscous effects
        // This could do with being in radians!
        stallAngle = stallAngleMin + (stallAngleMax - stallAngleMin) * Mathf.Exp(-aeroBody.EAB.aspectRatio / 2f);

        // Lifting line theory
        liftCurveSlope = 2f * Mathf.PI * aspectRatioCorrection_kAR * thicknessCorrection_kt;

        // Zero lift angle is set based on the amount of camber. This is physics based
        alpha_0 = -aeroBody.EAB.camberRatio;

        // Lift before and after stall
        CL_preStall = liftCurveSlope * (aeroBody.alpha - alpha_0);
        CL_postStall = 0.5f * CZmax * thicknessCorrection_kt * Mathf.Sin(2f * (aeroBody.alpha - alpha_0));

        // Sigmoid function for blending between pre and post stall
        // Wasting some calulcations here by converting to degrees...
        upperSigmoid = 1f / (1f + Mathf.Exp((stallAngle - Mathf.Rad2Deg * (alpha_0 - aeroBody.alpha)) * stallSharpness));
        lowerSigmoid = 1f / (1f + Mathf.Exp((-stallAngle - Mathf.Rad2Deg * (alpha_0 - aeroBody.alpha)) * stallSharpness));
        preStallFilter = lowerSigmoid - upperSigmoid;

        CL = preStallFilter * CL_preStall + (1 - preStallFilter) * CL_postStall;

        // Induced drag
        CD_induced = (1f / (Mathf.PI * aeroBody.EAB.aspectRatio)) * CL * CL;

        // Pitching moment at mid chord due to camber only active pre stall
        CM_0 = 0.25f * -liftCurveSlope * alpha_0 * preStallFilter;

        // Original equation for this is: z_cop = c/8 * (cos(2a) + 1)
        // Using trig identity for cos(2a) =  2*cos^2(a) - 1 to save on extra trig computations
        // Also, ax_c is the axis, not diameter so we x2 again
        CoP_z = 0.5f * aeroBody.EAB.midAxis * aeroBody.cosAlpha * aeroBody.cosAlpha;

        // Pitching moment because lift is applied at the centre of the body
        CM_delta = CL * CoP_z * aeroBody.cosAlpha / aeroBody.EAB.midAxis;
        CM = CM_0 + CM_delta;

        // Convert coefficients to forces and moments
        float qS = aeroBody.dynamicPressure * aeroBody.planformArea;
        Vector3 liftDirection = Vector3.Cross(aeroBody.aeroBodyFrame.windVelocity_normalised, aeroBody.angleOfAttackRotationVector);
        Vector3 lift_bodyFrame = qS * CL * liftDirection;
        Vector3 inducedDrag_bodyFrame = -CD_induced * aeroBody.dynamicPressure * aeroBody.profileArea * aeroBody.aeroBodyFrame.windVelocity_normalised;
        resultantForce_bodyFrame = lift_bodyFrame + inducedDrag_bodyFrame;

        // The minus sign here is dirty but I can't figure out why the pitching moment is always in the wrong direction?!
        resultantMoment_bodyFrame = new Vector3(-CM * qS * aeroBody.EAB.chord_c, 0, 0);
        resultantMoment_bodyFrame = aeroBody.TransformEABToBody(resultantMoment_bodyFrame);
    }

}
