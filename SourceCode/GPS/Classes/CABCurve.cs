﻿using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public partial class CGuidance
    {
        public void GetCurrentGuidanceLine(vec2 pivot, vec2 steer, double heading)
        {
            if (CurrentGMode == Mode.RecPath)
                UpdatePosition(pivot, steer, heading);
            else
            {
                if (CurrentGMode == Mode.Contour)
                    FindCurrentContourLine(pivot);

                if (currentGuidanceLine != null)
                    BuildCurrentCurveLine(pivot, heading, currentGuidanceLine);
                else
                {
                    curList = new Polyline();
                    isLocked = false;
                    return;
                }
                
                CalculateSteerAngle(pivot, steer, heading, isYouTurnTriggered ? ytList : curList);
            }
        }

        public void FindCurrentContourLine(vec2 pivot)
        {
            if ((mf.secondsSinceStart - lastSecondSearch) < (curList.points.Count < 2 ? 0.3 : 2.0)) return;

            lastSecondSearch = mf.secondsSinceStart;
            int ptCount;
            double minDistA = double.MaxValue;

            if (!isLocked)
            {
                int stripNum = -1;
                for (int s = 0; s < curveArr.Count; s++)
                {
                    if (curveArr[s].mode.HasFlag(Mode.Contour))
                    {
                        ptCount = curveArr[s].points.Count - (curveArr[s] == creatingContour ? backSpacing : 1);
                        if (ptCount < 1) continue;
                        double dist;
                        for (int p = 0; p < ptCount; p += 6)
                        {
                            dist = glm.Distance(pivot, curveArr[s].points[p]);
                            if (dist < minDistA)
                            {
                                minDistA = dist;
                                stripNum = s;
                            }
                        }

                        //catch the last point
                        dist = glm.Distance(pivot, curveArr[s].points[ptCount]);
                        if (dist < minDistA)
                        {
                            minDistA = dist;
                            stripNum = s;
                        }
                    }
                }

                if (stripNum < 0)
                {
                    isValid = false;
                    moveDistance = 0;
                    currentGuidanceLine = null;
                }
                else if (currentGuidanceLine != curveArr[stripNum])
                {
                    isValid = false;
                    moveDistance = 0;
                    currentGuidanceLine = curveArr[stripNum];
                }
            }
        }

        public void BuildCurrentCurveLine(vec2 pivot, double heading, CGuidanceLine refList)
        {
            //move the ABLine over based on the overlap amount set in vehicle
            double widthMinusOverlap = mf.tool.toolWidth - mf.tool.toolOverlap;
            
            if (!isValid || ((mf.secondsSinceStart - lastSecond) > 0.66 && (!mf.isAutoSteerBtnOn || mf.mc.steerSwitchHigh || refList == creatingContour)))
            {
                int refCount = refList.points.Count - (refList == creatingContour ? backSpacing : 0);
                if (refList.points.Count < 2)
                {
                    curList = new Polyline();
                    isLocked = false;
                    return;
                }

                if (!refList.mode.HasFlag(Mode.Contour))
                {
                    //guidance look ahead distance based on time or tool width at least 
                    double guidanceLookDist = Math.Max(mf.tool.toolWidth * 0.5, mf.mc.avgSpeed * 0.277777 * mf.guidanceLookAheadTime);
                    pivot = new vec2(pivot.easting + (Math.Sin(heading) * guidanceLookDist),
                                                    pivot.northing + (Math.Cos(heading) * guidanceLookDist));
                }

                pivot.GetCurrentSegment(refList.points, 0, refList.loop, out rA, out rB, refCount);
                if (rA < 0 || rB < 0)
                {
                    curList = new Polyline();
                    isLocked = false;
                    return;
                }

                //x2-x1
                double dx = refList.points[rB].easting - refList.points[rA].easting;
                //z2-z1
                double dz = refList.points[rB].northing - refList.points[rA].northing;

                //same way as line creation or not
                isHeadingSameWay = Math.PI - Math.Abs(Math.Abs(heading - Math.Atan2(dx, dz)) - Math.PI) < glm.PIBy2;

                distanceFromRefLine = pivot.FindDistanceToSegment(refList.points[rA], refList.points[rB], out _, true, rA != 0, rB != refList.points.Count - 1);

                double RefDist = (distanceFromRefLine + (isHeadingSameWay ? mf.tool.toolOffset : -mf.tool.toolOffset)) / widthMinusOverlap;
                if (double.IsInfinity(RefDist))
                    howManyPathsAway = 0;
                else if (RefDist < 0) howManyPathsAway = (int)(RefDist - 0.5);
                else howManyPathsAway = (int)(RefDist + 0.5);

                lastSecond = mf.secondsSinceStart;
            }

            if (refList?.mode.HasFlag(Mode.Contour) == true)
            {
                if (howManyPathsAway > 1)
                    howManyPathsAway = 1;
                if (howManyPathsAway < -1)
                    howManyPathsAway = -1;
            }

            if (!isValid || howManyPathsAway != oldHowManyPathsAway || (oldIsHeadingSameWay != isHeadingSameWay && mf.tool.toolOffset != 0))
            {
                if (refList == creatingContour && howManyPathsAway == 0)
                {
                    curList = new Polyline();
                    return;
                }

                double distAway = widthMinusOverlap * howManyPathsAway + (isHeadingSameWay ? -mf.tool.toolOffset : mf.tool.toolOffset);

                curList = BuildOffsetList(refList, distAway);
                isValid = true;

                if (mf.isSideGuideLines && (refList.mode.HasFlag(Mode.AB) || refList.loop) && howManyPathsAway != oldHowManyPathsAway)
                {
                    int Move = howManyPathsAway - oldHowManyPathsAway;

                    if (!isValid || Move < -5 || Move > 5 || Move == 0)
                    {
                        if (sideGuideLines.Count > 0) sideGuideLines.Clear();
                        for (double i = -2.5; i < 3; i++)
                        {
                            sideGuideLines.Add(BuildOffsetList(refList, widthMinusOverlap * (howManyPathsAway + i)));

                        }
                    }
                    else if (Move < 0)
                    {
                        for (int i = -1; i >= Move; i--)
                        {
                            sideGuideLines.RemoveAt(5);
                            sideGuideLines.Insert(0, BuildOffsetList(refList, widthMinusOverlap * (oldHowManyPathsAway - 2.5 + i)));
                        }
                    }
                    else
                    {
                        for (int i = 1; i <= Move; i++)
                        {
                            sideGuideLines.RemoveAt(0);
                            sideGuideLines.Add(BuildOffsetList(refList, widthMinusOverlap * (oldHowManyPathsAway + 2.5 + i)));
                        }
                    }
                }
                else if (!mf.isSideGuideLines) sideGuideLines.Clear();
                
                oldHowManyPathsAway = howManyPathsAway;
                oldIsHeadingSameWay = isHeadingSameWay;
            }
        }

        public Polyline BuildOffsetList(CGuidanceLine refList, double distAway)
        {
            //move the ABLine over based on the overlap amount set in vehicle
            double distSqAway = (distAway * distAway) - 0.01;

            Polyline buildList = new Polyline();

            int ptCount = refList.points.Count - (refList == creatingContour ? backSpacing : 1);

            int start = (refList == creatingContour) ? (rA - (isHeadingSameWay ? 10 : 50)) : 0;
            if (start < 0) start = 0;

            int end = (refList == creatingContour) ? (rA + (isHeadingSameWay ? 50 : 10)) : ptCount;
            if (end > ptCount) end = ptCount;

            buildList = refList.OffsetAndDissolvePolyline<Polyline>(distAway, abLength, start, end, true, mf.vehicle.minTurningRadius);


            if (refList.mode.HasFlag(Mode.Curve) && !refList.loop)
            {
                int cnt = buildList.points.Count;
                if (cnt > 6)
                {
                    vec2[] arr = new vec2[cnt];
                    buildList.points.CopyTo(arr);

                    for (int i = 1; i < (buildList.points.Count - 1); i++)
                    {
                        arr[i].easting = (buildList.points[i - 1].easting + buildList.points[i].easting + buildList.points[i + 1].easting) / 3;
                        arr[i].northing = (buildList.points[i - 1].northing + buildList.points[i].northing + buildList.points[i + 1].northing) / 3;
                    }
                    buildList.points.Clear();

                    if (mf.tool.isToolTrailing)
                    {
                        //depending on hitch is different profile of draft
                        double hitch;
                        if (mf.tool.isToolTBT && mf.tool.toolTankTrailingHitchLength < 0)
                        {
                            hitch = mf.tool.toolTankTrailingHitchLength * 0.85;
                            hitch += mf.tool.toolTrailingHitchLength * 0.65;
                        }
                        else hitch = mf.tool.toolTrailingHitchLength * 1.0;// - mf.vehicle.wheelbase;

                        //move the line forward based on hitch length ratio
                        for (int i = 0; i + 1 < arr.Length; i++)
                        {
                            double heading = Math.Atan2(arr[i + 1].easting - arr[i].easting, arr[i + 1].northing - arr[i].northing);

                            arr[i].easting -= Math.Sin(heading) * (hitch);
                            arr[i].northing -= Math.Cos(heading) * (hitch);
                        }
                    }

                    if (true)
                    {

                        for (int i = 0; i < arr.Length; i++)
                            buildList.points.Add(arr[i]);
                    }
                    else
                    {
                        //replace the array 
                        //curList.AddRange(arr);
                        cnt = arr.Length;
                        double distance;
                        double spacing = 0.5;

                        //add the first point of loop - it will be p1
                        buildList.points.Add(arr[0]);
                        buildList.points.Add(arr[1]);

                        for (int i = 0; i < cnt - 3; i++)
                        {
                            // add p1
                            buildList.points.Add(arr[i + 1]);

                            distance = glm.Distance(arr[i + 1], arr[i + 2]);

                            if (distance > spacing)
                            {
                                int loopTimes = (int)(distance / spacing + 1);
                                for (int j = 1; j < loopTimes; j++)
                                {
                                    buildList.points.Add(glm.Catmull(j / (double)(loopTimes), arr[i], arr[i + 1], arr[i + 2], arr[i + 3]));
                                }
                            }
                        }

                        buildList.points.Add(arr[cnt - 2]);
                        buildList.points.Add(arr[cnt - 1]);
                    }
                }
            }
            return buildList;
        }

        public void DrawGuidanceLines()
        {
            GL.LineWidth(lineWidth);

            if (CurrentGMode == Mode.RecPath && recList.Count > 0)
            {
                GL.Color3(0.98f, 0.92f, 0.460f);
                GL.Begin(PrimitiveType.LineStrip);
                for (int h = 0; h < recList.Count; h++) GL.Vertex3(recList[h].easting, recList[h].northing, 0);
                GL.End();

                if (!isRecordOn && currentPositonIndex < recList.Count)
                {
                    //Draw lookahead Point
                    GL.PointSize(16.0f);
                    GL.Begin(PrimitiveType.Points);

                    GL.Color3(1.0f, 0.5f, 0.95f);
                    GL.Vertex3(recList[currentPositonIndex].easting, recList[currentPositonIndex].northing, 0);
                    GL.End();
                    GL.PointSize(1.0f);
                }
            }

            if (EditGuidanceLine != null && EditGuidanceLine.points.Count > 1)
            {
                GL.Color3(0.95f, 0.42f, 0.750f);
                GL.Begin(PrimitiveType.LineStrip);

                for (int h = 0; h < EditGuidanceLine.points.Count; h++)
                {
                    if (h == 0 && !EditGuidanceLine.loop)
                    {
                        double heading = Math.Atan2(EditGuidanceLine.points[1].easting - EditGuidanceLine.points[0].easting, EditGuidanceLine.points[1].northing - EditGuidanceLine.points[0].northing);

                        GL.Vertex3(EditGuidanceLine.points[h].easting - (Math.Sin(heading) * abLength), EditGuidanceLine.points[h].northing - (Math.Cos(heading) * abLength), 0.0);
                    }

                    GL.Vertex3(EditGuidanceLine.points[h].easting, EditGuidanceLine.points[h].northing, 0);

                    if (h == EditGuidanceLine.points.Count - 1 && EditGuidanceLine.mode.HasFlag(Mode.AB))
                    {
                        double heading = Math.Atan2(EditGuidanceLine.points[h].easting - EditGuidanceLine.points[h - 1].easting,
                            EditGuidanceLine.points[h].northing - EditGuidanceLine.points[h - 1].northing);

                        GL.Vertex3(EditGuidanceLine.points[h].easting + (Math.Sin(heading) * abLength), EditGuidanceLine.points[h].northing + (Math.Cos(heading) * abLength), 0.0);
                    }
                }

                if (!EditGuidanceLine.mode.HasFlag(Mode.AB))
                {
                    GL.Color3(0.930f, 0.0692f, 0.260f);
                    GL.Vertex3(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing, 0);
                }
                GL.End();
            }
            else
            {
                if (currentGuidanceLine != null && currentGuidanceLine.points.Count > 1)
                {
                    if (isSmoothWindowOpen)
                        GL.Color3(0.930f, 0.92f, 0.260f);
                    else
                        GL.Color3(0.96, 0.2f, 0.2f);
                    GL.Enable(EnableCap.LineStipple);
                    GL.LineStipple(1, 0x0F00);
                    bool Extend = false;
                    if (currentGuidanceLine.mode.HasFlag(Mode.Contour))
                    {
                        if (isLocked)
                            GL.Color3(0.983f, 0.2f, 0.20f);
                        else
                            GL.Color3(0.3f, 0.982f, 0.0f);
                        GL.Begin(PrimitiveType.Points);
                    }
                    else if (currentGuidanceLine.loop)
                        GL.Begin(PrimitiveType.LineLoop);
                    else
                    {
                        Extend = true;
                        GL.Begin(PrimitiveType.LineStrip);
                    }

                    for (int h = 0; h < currentGuidanceLine.points.Count; h++)
                    {
                        if (Extend && h == 0)
                        {
                            double heading = Math.Atan2(currentGuidanceLine.points[1].easting - currentGuidanceLine.points[0].easting, currentGuidanceLine.points[1].northing - currentGuidanceLine.points[0].northing);

                            GL.Vertex3(currentGuidanceLine.points[h].easting - (Math.Sin(heading) * abLength), currentGuidanceLine.points[h].northing - (Math.Cos(heading) * abLength), 0.0);
                        }

                        GL.Vertex3(currentGuidanceLine.points[h].easting, currentGuidanceLine.points[h].northing, 0);

                        if (Extend && h == currentGuidanceLine.points.Count - 1)
                        {
                            double heading = Math.Atan2(currentGuidanceLine.points[h].easting - currentGuidanceLine.points[h - 1].easting,
                                currentGuidanceLine.points[h].northing - currentGuidanceLine.points[h - 1].northing);

                            GL.Vertex3(currentGuidanceLine.points[h].easting + (Math.Sin(heading) * abLength), currentGuidanceLine.points[h].northing + (Math.Cos(heading) * abLength), 0.0);
                        }
                    }
                    GL.End();
                    GL.Disable(EnableCap.LineStipple);
                }

                if (curList.points.Count > 1)
                {
                    if (mf.isSideGuideLines && mf.worldManager.camSetDistance > mf.tool.toolWidth * -120)
                    {
                        GL.Color3(0.756f, 0.7650f, 0.7650f);
                        GL.Enable(EnableCap.LineStipple);
                        GL.LineStipple(1, 0x0303);

                        for (int i = 0; i < sideGuideLines.Count; i++)
                        {
                            GL.Begin(sideGuideLines[i].loop ? PrimitiveType.LineLoop : PrimitiveType.LineStrip);
                            for (int j = 0; j < sideGuideLines[i].points.Count; j++)
                            {
                                GL.Vertex3(sideGuideLines[i].points[j].easting, sideGuideLines[i].points[j].northing, 0);
                            }
                            GL.End();
                        }

                        GL.Disable(EnableCap.LineStipple);
                    }

                    GL.Color3(0.95f, 0.2f, 0.95f);
                    if (curList.loop)
                        GL.Begin(PrimitiveType.LineLoop);
                    else
                        GL.Begin(PrimitiveType.LineStrip);
                    for (int h = 0; h < curList.points.Count; h++)
                        GL.Vertex3(curList.points[h].easting, curList.points[h].northing, 0);
                    GL.End();

                    if (currentGuidanceLine?.mode.HasFlag(Mode.Contour) == true)
                    {
                        GL.Begin(PrimitiveType.Points);
                        GL.Color3(0.87f, 08.7f, 0.25f);
                        for (int h = 0; h < curList.points.Count; h++)
                            GL.Vertex3(curList.points[h].easting, curList.points[h].northing, 0);
                        GL.End();
                    }

                    if (!mf.isStanleyUsed && mf.worldManager.camSetDistance > -200)
                    {
                        //Draw lookahead Point
                        GL.PointSize(8.0f);
                        GL.Begin(PrimitiveType.Points);
                        GL.Color3(1.0f, 0.95f, 0.195f);
                        GL.Vertex3(goalPoint.easting, goalPoint.northing, 0.0);
                        GL.End();
                    }

                    if (OffsetList.points.Count > 1)
                    {
                        GL.Enable(EnableCap.LineStipple);
                        GL.LineStipple(1, 0xFC00);
                        GL.Color3(0.95f, 0.5f, 0.95f);
                        GL.Begin(PrimitiveType.LineStrip);
                        for (int i = 0; i < OffsetList.points.Count; i++)
                        {
                            GL.Vertex3(OffsetList.points[i].easting, OffsetList.points[i].northing, 0);
                        }
                        GL.End();
                        GL.Disable(EnableCap.LineStipple);
                    }

                    if (ytList.points.Count > 1)
                    {
                        GL.PointSize(lineWidth * 2);

                        if (isYouTurnTriggered)
                            GL.Color3(0.95f, 0.5f, 0.95f);
                        else if (isOutOfBounds)
                            GL.Color3(0.9495f, 0.395f, 0.325f);
                        else
                            GL.Color3(0.395f, 0.925f, 0.30f);

                        GL.Begin(PrimitiveType.Points);
                        for (int i = 0; i < ytList.points.Count; i++)
                        {
                            GL.Vertex3(ytList.points[i].easting, ytList.points[i].northing, 0);
                        }
                        GL.End();
                    }
                }
            }
            GL.PointSize(lineWidth);
        }

        public void MoveGuidanceLine(CGuidanceLine GuidanceLine, double dist)
        {
            isValid = false;

            if (GuidanceLine != null)
            {
                moveDistance += isHeadingSameWay ? dist : -dist;

                GuidanceLine.points = GuidanceLine.OffsetPolyline<Polyline>(isHeadingSameWay ? dist : -dist);
            }
        }

        public void ReverseGuidanceLine(CGuidanceLine guidanceLine)
        {
            isValid = false;

            if (guidanceLine != null)
            {
                int cnt = guidanceLine.points.Count;
                if (cnt > 1)
                {
                    if (guidanceLine.mode.HasFlag(Mode.AB))
                    {
                        double heading = Math.Atan2(guidanceLine.points[0].easting - guidanceLine.points[1].easting,
                           guidanceLine.points[0].northing - guidanceLine.points[1].northing);

                        guidanceLine.points[0] = new vec2(guidanceLine.points[0].easting, guidanceLine.points[0].northing);
                        guidanceLine.points[1] = new vec2(guidanceLine.points[0].easting + Math.Sin(heading), guidanceLine.points[0].northing + Math.Cos(heading));
                    }
                    else
                    {
                        guidanceLine.points.Reverse();
                    }
                }
            }
        }
    }
}