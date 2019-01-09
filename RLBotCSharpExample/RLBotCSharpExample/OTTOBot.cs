using System;
using RLBotDotNet;
using rlbot.flat;
using System.Collections.Generic;

namespace OTTOBot
{
    // We want to our bot to derive from Bot, and then implement its abstract methods.
    class OTTOBot : Bot
    {
        // We want the constructor for ExampleBot to extend from Bot, but we don't want to add anything to it.
        public OTTOBot(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex) { }
        float alpha = 0;

        public struct OTTOBotProperties
        {
            public string mode;
            public string location;
            public float distanceToBall;
            public double angleToTarget;
            public int boost;
            public double pitch;
            public double roll;
            public double yaw;
            public Vector3 target;
            public Vector3 pos;
            public Vector3 velocity;
            public Vector3 closestBoost;
            public Vector3 forward;
        };

        public struct FieldProperties
        {
            public Vector3 ballVelocity;
            public Vector3 ballPos;
            public Vector3 oppGoal;
            public Vector3 ownGoal;
            public List<Vector3> boost;
        }

        public struct BezierCurve
        {
            public Vector3 p1;
            public Vector3 cp;
            public Vector3 p2;
            public Vector3 GetPoint(float t)
            {
                return new Vector3(
                    (1 - t) * (1 - t) * p1.x + 2 * (1 - t) * t * cp.x + t * t * p2.x,
                    (1 - t) * (1 - t) * p1.y + 2 * (1 - t) * t * cp.y + t * t * p2.y,
                    (1 - t) * (1 - t) * p1.z + 2 * (1 - t) * t * cp.z + t * t * p2.z);
            }
        }

        OTTOBotProperties ottoBot;
        FieldProperties field;
        BezierCurve bezierCurve;
        Controller controller;
        PlayerInfo playerInfo;

        public override Controller GetOutput(GameTickPacket gameTickPacket)
        {
            // This controller object will be returned at the end of the method.
            // This controller will contain all the inputs that we want the bot to perform.
            controller = new Controller();

            // Wrap gameTickPacket retrieving in a try-catch so that the bot doesn't crash whenever a value isn't present.
            // A value may not be present if it was not sent.
            // These are nullables so trying to get them when they're null will cause errors, therefore we wrap in try-catch.
            try
            {
                playerInfo = gameTickPacket.Players(this.index).Value;
                BallInfo ballInfo = gameTickPacket.Ball.Value;

                field.ballVelocity = new Vector3(ballInfo.Physics.Value.Velocity.Value.X, ballInfo.Physics.Value.Velocity.Value.Y, ballInfo.Physics.Value.Velocity.Value.Z);
                field.ballPos = new Vector3(gameTickPacket.Ball.Value.Physics.Value.Location.Value.X, gameTickPacket.Ball.Value.Physics.Value.Location.Value.Y, gameTickPacket.Ball.Value.Physics.Value.Location.Value.Z);
                field.ownGoal = team == 0 ? new Vector3(0, -5420, 0) : new Vector3(0, 5420, 0);
                field.oppGoal = team == 0 ? new Vector3(0, 5420, 0) : new Vector3(0, -5420, 0);
                field.boost = new List<Vector3>();
                field.boost.Add(new Vector3(-3072.0f, -4096.0f, 0));
                field.boost.Add(new Vector3(3072.0f, -4096.0f, 0));
                field.boost.Add(new Vector3(-3584.0f, 0.0f, 0));
                field.boost.Add(new Vector3(3584.0f, 0.0f, 0));
                field.boost.Add(new Vector3(-3072.0f, 4096.0f, 0));
                field.boost.Add(new Vector3(-3072.0f, 4096.0f, 0));

                ottoBot.pos = new Vector3(gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value.X, gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value.Y, gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value.Z);
                ottoBot.boost = gameTickPacket.Players(this.index).Value.Boost;
                ottoBot.distanceToBall = field.ballPos.Magnitude(ottoBot.pos);
                ottoBot.pitch = gameTickPacket.Players(this.index).Value.Physics.Value.Rotation.Value.Pitch;
                ottoBot.yaw = gameTickPacket.Players(this.index).Value.Physics.Value.Rotation.Value.Yaw;
                ottoBot.roll = gameTickPacket.Players(this.index).Value.Physics.Value.Rotation.Value.Roll;
                ottoBot.velocity = new Vector3(playerInfo.Physics.Value.Velocity.Value.X, playerInfo.Physics.Value.Velocity.Value.Y, playerInfo.Physics.Value.Velocity.Value.Z);
                ottoBot.forward = new Vector3((float)Math.Cos(ottoBot.yaw), (float)Math.Sin(ottoBot.yaw), 0);
                ottoBot.closestBoost = ClosestBoost();
                
                SetMode();
                SetLocation();

                switch (ottoBot.mode)
                {
                    case "Attack":
                        Attack();
                        break;
                    case "Defend":
                        Defend();
                        break;
                    case "Get Boost":
                        GetBoost();
                        break;
                    default:
                        Console.WriteLine("No Mode Set!");
                        break;
                }

                UpdateOttoBot();  
                DrawDebugInfo();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }

            return controller;
        }

        private void UpdateOttoBot()
        {
            //calculate bot target
            ottoBot.target = bezierCurve.GetPoint(alpha);

            //point on path bots targeting
            if (ottoBot.target.Magnitude(ottoBot.pos) < 75 && alpha < 1)
                alpha += .01f;
            else if (ottoBot.target.Magnitude(ottoBot.pos) > 100 && alpha > 0)
                alpha -= .01f;

            //calculate angle for bot to drive to target
            double botToTargetAngle = Math.Atan2(ottoBot.target.y - ottoBot.pos.y, ottoBot.target.x - ottoBot.pos.x);
            ottoBot.angleToTarget = CorrectAngle(botToTargetAngle - ottoBot.yaw);
            
            if(RadToDeg(ottoBot.angleToTarget) > 110 || RadToDeg(ottoBot.angleToTarget) < -100)
                controller.Handbrake = true;

            //steer
            if (ottoBot.angleToTarget > 0)
                controller.Steer = 1;
            else
                controller.Steer = -1;

            //speed
            if (field.ballPos.z > 200 && field.ballPos.Magnitude(ottoBot.pos) < 1000 && ottoBot.mode == "Attack")
                controller.Throttle = 0;
            else
                controller.Throttle = 1;

            //boost
            if (field.ballPos.x == 0 && field.ballPos.y == 0 && field.ballVelocity.x == 0 && field.ballVelocity.y == 0)
                controller.Boost = true;
            else if (field.ballPos.Magnitude(ottoBot.pos) > 2000 && ottoBot.mode == "Attack")
                controller.Boost = true;
            else if (ottoBot.mode == "Defend")
                controller.Boost = true;
            else
                controller.Boost = false;
        }

        //identify if the bot needs to attack defend etc..
        private void SetMode()
        {
            //if bot is farther away from the oppnent goal then the ball is attack otherwise defend
            if (ottoBot.pos.Magnitude(field.oppGoal) > field.ballPos.Magnitude(field.oppGoal))
            {
                ottoBot.mode = "Attack";
            }
            //else if(ottoBot.boost < 5)
            //{
            //    ottoBot.mode = "Get Boost";
            //}
            else
            {
                ottoBot.mode = "Defend";
            }
        }

        private void SetLocation()
        {
            //if bot is farther away from the oppnent goal then the ball is attack otherwise defend
            if (ottoBot.pos.y > 5120 || ottoBot.pos.y < -5120)
            {
                ottoBot.location = "Goal";
            }
            else
            {
                ottoBot.location = "Field";
            }
        }

        private void Attack()
        {
            //calulate balls angle to goal
            Vector3 headingToGoalFromBall = field.ballPos.Direction(field.oppGoal);
            Vector3 headingToGoalFromBallNormalized = headingToGoalFromBall.Normalize(field.ballPos);

            //calculate control point
            bezierCurve.cp = new Vector3(
                field.ballPos.x - (headingToGoalFromBallNormalized.x * (ottoBot.distanceToBall/2)),
                field.ballPos.y - (headingToGoalFromBallNormalized.y * (ottoBot.distanceToBall/2)),
                field.ballPos.z - (headingToGoalFromBallNormalized.z * (ottoBot.distanceToBall / 2)));

            if (bezierCurve.cp.x > 4096)
                bezierCurve.cp.x = 4096;// field.ballPos.x - (headingToGoalFromBallNormalized.x * (-ottoBot.distanceToBall / 2));
            else if (bezierCurve.cp.x < -4096)
                bezierCurve.cp.x = -4096;

            if (bezierCurve.cp.x > 5120)
                bezierCurve.cp.x = 5120;
            else if (bezierCurve.cp.x < -5120)
                bezierCurve.cp.x = -5120;

            if(ottoBot.location == "Goal")
            {
                bezierCurve.cp.x = 0;
            }

            bezierCurve.p1 = ottoBot.pos;
            bezierCurve.p2 = field.ballPos;
        }

        private void Defend()
        {
            bezierCurve.p1 = ottoBot.pos;
            bezierCurve.p2 = field.ownGoal;

            bezierCurve.cp = new Vector3(
                (bezierCurve.p1.x + bezierCurve.p2.x) / 2,
                (bezierCurve.p1.y + bezierCurve.p2.y) / 2,
                (bezierCurve.p1.z + bezierCurve.p2.z) / 2);

        }

        private void GetBoost()
        {
            bezierCurve.p1 = ottoBot.pos;
            bezierCurve.p2 = ottoBot.closestBoost;
            bezierCurve.cp = new Vector3(
                (bezierCurve.p1.x + bezierCurve.p2.x) / 2,
                (bezierCurve.p1.y + bezierCurve.p2.y) / 2,
                (bezierCurve.p1.z + bezierCurve.p2.z) / 2);
        }

        private Vector3 ClosestBoost()
        {
            if (ottoBot.closestBoost == null)
                ottoBot.closestBoost = field.boost[2];
            Vector3 closest = field.boost[2];
            foreach (Vector3 v in field.boost)
            {
                if (v.Magnitude(ottoBot.pos) < closest.Magnitude(ottoBot.pos))
                    closest = v;
            }
            return closest;
        }

        private double CorrectAngle(double botFrontToTargetAngle)
        {
            if (botFrontToTargetAngle < -Math.PI)
                botFrontToTargetAngle += 2 * Math.PI;
            if (botFrontToTargetAngle > Math.PI)
                botFrontToTargetAngle -= 2 * Math.PI;
            return botFrontToTargetAngle;
        }

        float Lerp(float a, float b, float t)
        {
            return (1f - t) * a + t * b;
        }

        private float RadToDeg(double r)
        {
            return (float)(r * 180 / Math.PI);
        }

        private void DrawLine3D(Vector3 start, Vector3 end, System.Windows.Media.Color c)
        {
            Renderer.DrawLine3D(c, new System.Numerics.Vector3(start.x, start.y, start.z), new System.Numerics.Vector3(end.x, end.y, end.z));
        }

        private void DrawDebugInfo()
        {
            int y = 0;
            if (playerInfo.Team == 0)
                y = 15;
            if (playerInfo.Team == 1)
                y = 75;

            //bot info
            if (playerInfo.Team == 0)
                Renderer.DrawRectangle2D(System.Windows.Media.Color.FromRgb(0, 0, 0), new System.Numerics.Vector2(10, 10), 225, 180, true);
            Renderer.DrawString2D("Car Info", System.Windows.Media.Color.FromRgb(255, 0, 0), new System.Numerics.Vector2(15, y), 1, 1);
            y += 15;
            Renderer.DrawString2D("Mode: " + ottoBot.mode, System.Windows.Media.Color.FromRgb(255, 255, 255), new System.Numerics.Vector2(15, y), 1, 1);
            y += 15;
            Renderer.DrawString2D("Location: " + ottoBot.location, System.Windows.Media.Color.FromRgb(255, 255, 255), new System.Numerics.Vector2(15, y), 1, 1);
            y += 15;
            Renderer.DrawString2D("T-Angle: " + RadToDeg(ottoBot.angleToTarget), System.Windows.Media.Color.FromRgb(255, 255, 255), new System.Numerics.Vector2(15, y), 1, 1);
            y += 15;

            //field info
            if (playerInfo.Team == 1)
            {
                Renderer.DrawString2D("Field Info", System.Windows.Media.Color.FromRgb(255, 0, 0), new System.Numerics.Vector2(15, y), 1, 1);
                y += 15;
                Renderer.DrawString2D("Ball Pos: (" + field.ballPos.x + ", " + field.ballPos.y + ")", System.Windows.Media.Color.FromRgb(255, 255, 255), new System.Numerics.Vector2(15, y), 1, 1);
                y += 15;
                Renderer.DrawString2D("Ball Vel: (" + field.ballVelocity.x + ", " + field.ballVelocity.y + ")", System.Windows.Media.Color.FromRgb(255, 255, 255), new System.Numerics.Vector2(15, y), 1, 1);
            }

            //draw line to target
            DrawLine3D(ottoBot.target, ottoBot.pos, System.Windows.Media.Color.FromRgb(255, 255, 255));

            //draw path
            float dt = 0.01f;
            float time = 0;

            while (time + dt <= 1)
            {
                time += dt;
                float a = time / 1;
                float a1 = (time + dt) / 1;
                if (a1 <= 1)
                {
                    Vector3 point = bezierCurve.GetPoint(a);
                    Vector3 point1 = bezierCurve.GetPoint(a1);
                    DrawLine3D(point, point1, System.Windows.Media.Color.FromRgb(0, 255, 0));
                }
                else
                {
                    break;
                }
            }

            DrawLine3D(bezierCurve.p1, bezierCurve.cp, System.Windows.Media.Color.FromRgb(0, 255, 0));
            DrawLine3D(bezierCurve.cp, bezierCurve.p2, System.Windows.Media.Color.FromRgb(0, 255, 0));
        }
    }
}

public class Vector3
{
    public float x;
    public float y;
    public float z;
    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
    }

    public Vector3 Normalize(Vector3 destination)
    {
        float m = Magnitude(destination);
        return new Vector3(x / m, y / m, z / m);
    }

    public float Magnitude(Vector3 destination)
    {
        return (float)Math.Sqrt(Math.Pow(destination.x - x, 2) + Math.Pow(destination.y - y, 2) + Math.Pow(destination.z - z, 2));
    }

    public Vector3 Direction(Vector3 destination)
    {
        return new Vector3(destination.x - x, destination.y - y, destination.z - z);
    }

    public Vector3 Multiply(float n)
    {
        return new Vector3(x * n, x * y, x * z);
    }

    public Vector3 Add(Vector3 v)
    {
        return new Vector3(x + v.x, y + v.y, z + v.z);
    }
}

