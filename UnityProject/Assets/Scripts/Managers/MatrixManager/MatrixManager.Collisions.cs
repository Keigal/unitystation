
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Collision-related stuff
/// </summary>
public partial class MatrixManager
{
	/// <summary>
	/// Currently moving matrices. Should be monitored for new intersections with other matrix bounds
	/// </summary>
	private List<MatrixInfo> movingMatrices = new List<MatrixInfo>();

	/// <summary>
	/// bounds intersections that should be actively checked for actual tile collisions
	/// </summary>
	private List<MatrixIntersection> trackedIntersections = new List<MatrixIntersection>();

	public List<MatrixIntersection> TrackedIntersections => trackedIntersections;

	private void InitCollisions()
	{
		if (!Application.isPlaying || !CustomNetworkManager.Instance._isServer)
		{
			return;
		}

		foreach ( var movableMatrix in MovableMatrices )
		{
			movableMatrix.MatrixMove.OnStart.AddListener( () =>
			{
				if ( !movingMatrices.Contains( movableMatrix ) )
				{
					movingMatrices.Add( movableMatrix );
				}
			} );
			movableMatrix.MatrixMove.OnStop.AddListener( () =>
			{
				if ( movingMatrices.Contains( movableMatrix ) )
				{
					movingMatrices.Remove( movableMatrix );
					trackedIntersections.RemoveAll( intersection => intersection.Matrix1 == movableMatrix );
				}
			} );
		}
	}

	private void RefreshIntersections()
	{
		if ( movingMatrices.Count == 0 )
		{
			if ( trackedIntersections.Count > 0 )
			{
				trackedIntersections.Clear();
			}
			return;
		}

		PruneTracking();
		TrackNewIntersections();

		void PruneTracking()
		{
			List<MatrixIntersection> toRemove = null;
			List<MatrixIntersection> toUpdate = null;

			foreach ( var trackedIntersection in trackedIntersections )
			{
				if ( trackedIntersection.Matrix1.BoundsIntersect( trackedIntersection.Matrix2, out Rect hotZone ) )
				{ //refresh rect
					if ( toUpdate == null )
					{
						toUpdate = new List<MatrixIntersection>();
					}

					toUpdate.Add( new MatrixIntersection
					{
						Matrix1 = trackedIntersection.Matrix1,
						Matrix2 = trackedIntersection.Matrix2,
						Rect = hotZone
					} );
				}
				else
				{ //stop tracking non-intersecting ones
					if ( toRemove == null )
					{
						toRemove = new List<MatrixIntersection>();
					}

					toRemove.Add( trackedIntersection );
				}
			}

			if ( toUpdate != null )
			{
				foreach ( var updateMe in toUpdate )
				{
					trackedIntersections.Remove( updateMe );
					trackedIntersections.Add( updateMe );
				}
			}

			if ( toRemove != null )
			{
				foreach ( var removeMe in toRemove )
				{
					trackedIntersections.Remove( removeMe );
				}
			}

		}

		void TrackNewIntersections()
		{
			foreach ( var movingMatrix in movingMatrices )
			{
				var intersections = GetIntersections( movingMatrix );
				if ( intersections == noIntersections )
				{
					continue;
				}

				foreach ( var intersection in intersections )
				{
					if ( trackedIntersections.Contains( intersection ) )
					{
						continue;
					}

					trackedIntersections.Add( intersection );
				}
			}
		}
	}

	private static readonly MatrixIntersection[] noIntersections = new MatrixIntersection[0];

	private MatrixIntersection[] GetIntersections( MatrixInfo matrix )
	{
		List<MatrixIntersection> intersections = null;
		foreach ( var otherMatrix in ActiveMatrices )
		{
			if ( matrix == otherMatrix )
			{
				continue;
			}
			if ( matrix.BoundsIntersect( otherMatrix, out Rect hotZone ) )
			{
				if ( intersections == null )
				{
					intersections = new List<MatrixIntersection>();
				}

				intersections.Add( new MatrixIntersection
				{
					Matrix1 = matrix,
					Matrix2 = otherMatrix,
					Rect = hotZone
				} );
			}
		}

		if ( intersections != null )
		{
			return intersections.ToArray();
		}
		return noIntersections;
	}

	private void Update()
	{
		if (!Application.isPlaying || !CustomNetworkManager.Instance._isServer)
		{
			return;
		}

		RefreshIntersections();

		if ( trackedIntersections.Count == 0 )
		{
			return;
		}

		for ( var i = trackedIntersections.Count - 1; i >= 0; i-- )
		{
			CheckTileCollisions( trackedIntersections[i] );
		}
	}

	private void CheckTileCollisions( MatrixIntersection i )
	{
//		List<Vector3Int> nonEmpty = null;
		Vector3Int cellPos1;
		Vector3Int cellPos2;
		int collisions = 0;
		float speedDifference = 0f;
		foreach ( Vector3Int worldPos in i.Rect.ToBoundsInt().allPositionsWithin )
		{
			cellPos1 = i.Matrix1.MetaTileMap.WorldToCell( worldPos );
			if ( i.Matrix1.Matrix.IsEmptyAt( cellPos1, true ) )
			{
				continue;
			}

			cellPos2 = i.Matrix2.MetaTileMap.WorldToCell( worldPos );
			if ( i.Matrix2.Matrix.IsEmptyAt( cellPos2, true ) )
			{
				continue;
			}

			collisions++;

			//todo: placeholder, must take movement vectors in account!
			speedDifference = i.Matrix1.Speed + i.Matrix2.Speed;

			//
			// ******** DESTROY STUFF!!! ********
			//

			float damage = 50 * speedDifference;

			//Integrity
			foreach ( var integrity in GetAt<Integrity>(worldPos, true) )
			{
				integrity.ApplyDamage( damage, AttackType.Melee, DamageType.Brute );
			}

			//LivingHealthBehaviour
			foreach ( var healthBehaviour in GetAt<LivingHealthBehaviour>(worldPos, true) )
			{
				healthBehaviour.ApplyDamage( null, damage, AttackType.Melee, DamageType.Brute, BodyPartType.Chest.Randomize( 0 ) );
			}

			//TilemapDamage
			foreach ( var damageableLayer in i.Matrix1.MetaTileMap.DamageableLayers )
			{
				if ( Random.value >= 0.5 )
				{
					i.Matrix1.TileChangeManager.RemoveTile( cellPos1, damageableLayer.LayerType );
				} else
				{
					if ( damageableLayer.LayerType == LayerType.Floors )
					{
						damageableLayer.TilemapDamage.TryScorch( cellPos1 );
					}

					damageableLayer.TilemapDamage.DoMeleeDamage( cellPos1.To2Int(), i.Matrix2.GameObject, ( int ) damage );
				}
			}
			foreach ( var damageableLayer in i.Matrix2.MetaTileMap.DamageableLayers )
			{
				if ( Random.value >= 0.5 )
				{
					i.Matrix2.TileChangeManager.RemoveTile( cellPos2, damageableLayer.LayerType );
				} else
				{
					if ( damageableLayer.LayerType == LayerType.Floors )
					{
						damageableLayer.TilemapDamage.TryScorch( cellPos2 );
					}

					damageableLayer.TilemapDamage.DoMeleeDamage( cellPos2.To2Int(), i.Matrix1.GameObject, ( int ) damage );
				}
			}

			//Heat shit up
			i.Matrix1.ReactionManager.ExposeHotspot( cellPos1, 150 * collisions, collisions/10f );
			i.Matrix2.ReactionManager.ExposeHotspot( cellPos2, 150 * collisions, collisions/10f );

			//Other
			foreach ( var layer in otherLayers )
			{
				i.Matrix1.TileChangeManager.RemoveTile( cellPos1, layer );
				i.Matrix2.TileChangeManager.RemoveTile( cellPos2, layer );
			}
		}

		if ( collisions > 0 )
		{
			ExplosionUtils.PlaySoundAndShake(
				i.Rect.position.RoundToInt(),
				(byte) Mathf.Clamp(collisions*4, 5, byte.MaxValue),
				Mathf.Clamp(collisions, 15, 60)
				);
			SlowDown( i, collisions );
		}
	}
	private static LayerType[] otherLayers = { LayerType.Effects, LayerType.Base, LayerType.Objects };

	//todo: consider mass, wall hardness, speed. reduce speed depending on destroyed tile amount and mass
	private void SlowDown( MatrixIntersection i, int collisions )
	{
		if ( i.Matrix1.IsMovable && i.Matrix1.MatrixMove.isMovingServer )
		{
			InternalSlowDown( i.Matrix1 );
		}
		if ( i.Matrix2.IsMovable && i.Matrix2.MatrixMove.isMovingServer )
		{
			InternalSlowDown( i.Matrix2 );
		}

		void InternalSlowDown( MatrixInfo info )
		{
			float slowdownFactor = Mathf.Clamp(
				1f - ( Mathf.Clamp( collisions, 1, 50 ) / 100f ) + info.Mass,
				0.1f,
				0.95f
				);
			float speed = ( info.MatrixMove.State.Speed * slowdownFactor ) - 0.1f;
			info.MatrixMove.SetSpeed( speed < 1 ? 0 : speed );
		}
	}

	private void OnDrawGizmos()
	{
		if (!Application.isPlaying || !IsInitialized) return;
		foreach ( var intersection in Instance.TrackedIntersections )
		{
			Gizmos.color = Color.red;
			DebugGizmoUtils.DrawRect( intersection.Matrix1.WorldBounds );
			Gizmos.color = Color.blue;
			DebugGizmoUtils.DrawRect( intersection.Matrix2.WorldBounds );
			Gizmos.color = Color.yellow;
			DebugGizmoUtils.DrawRect( intersection.Rect );
		}
	}
}

/// <summary>
/// First and second matrix are swappable – intersections (m1,m2) and (m2,m1) will be considered equal.
/// Rect isn't checked for equality
/// </summary>
public struct MatrixIntersection
{
	public MatrixInfo Matrix1;
	public MatrixInfo Matrix2;
	public Rect Rect;

	public override int GetHashCode()
	{
		return Matrix1.GetHashCode() ^ Matrix2.GetHashCode();
	}

	public bool Equals( MatrixIntersection other )
	{
		return (Matrix1.Equals( other.Matrix1 ) && Matrix2.Equals( other.Matrix2 ))
		       || (Matrix1.Equals( other.Matrix2 ) && Matrix2.Equals( other.Matrix1 ));	}

	public override bool Equals( object obj )
	{
		return obj is MatrixIntersection other && Equals( other );
	}

	public static bool operator ==( MatrixIntersection left, MatrixIntersection right )
	{
		return left.Equals( right );
	}

	public static bool operator !=( MatrixIntersection left, MatrixIntersection right )
	{
		return !left.Equals( right );
	}
}