using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ImprovedSwimming
{
    [BepInPlugin("projjm.improvedswimming", "Improved Swimming", "1.0.0")]
    [BepInProcess("valheim.exe")]
    public class ImprovedSwimming : BaseUnityPlugin
    {

		private static ConfigEntry<KeyCode> swimFasterKey;
		private static ConfigEntry<float> swimFasterSpeedMultiplier;
		private static ConfigEntry<float> swimFasterStaminaMultiplier;
		private static ConfigEntry<float> swimStaminaDrainMaxSkill;
		private static ConfigEntry<float> swimStaminaDrainMinSkill;
		private static ConfigEntry<float> swimSpeedMultiplierMin;
		private static ConfigEntry<float> swimSpeedMultiplierMax;
		private static ConfigEntry<float> swimIdleStaminaRegenMultiplierMin;
		private static ConfigEntry<float> swimIdleStaminaRegenMultiplierMax;

		private static int forward_speed = 0;
		private static int sideway_speed = 0;
		private static int turn_speed = 0;
		private static int inWater = 0;
		private static int onGround = 0;
		private static int encumbered = 0;
		private static int flying = 0;

		private static bool swimFasterKeyHeld;

		private readonly Harmony harmony = new Harmony("projjm.improvedswimming");
        void Awake()
        {
			swimFasterKey = Config.Bind("Fast Swim", "swimFasterKey", KeyCode.LeftShift, "The key to swim faster.");
			swimFasterSpeedMultiplier = Config.Bind("Fast Swim", "swimFasterSpeedModifier", 2.0f, "Speed multiplier when swimming faster");
			swimFasterStaminaMultiplier = Config.Bind("Fast Swim", "swimFasterStaminaModifier", 3f, "Stamina drain multiplier when swimming faster");
			swimStaminaDrainMinSkill = Config.Bind("Normal Swim", "swimStaminaDrainMinSkill", 4f, "Stamina drain when Swimming is at min level [Base game default is 5]");
			swimStaminaDrainMaxSkill = Config.Bind("Normal Swim", "swimStaminaDrainMaxSkill", 2f, "Stamina drain when Swimming is at max level [Base game default is 2]");
			swimSpeedMultiplierMin = Config.Bind("Normal Swim", "swimSpeedMultiplierMin", 0.85f, "Speed multiplier when Swimming is at min level");
			swimSpeedMultiplierMax = Config.Bind("Normal Swim", "swimSpeedMultiplierMax", 1.5f, "Speed multiplier when Swimming is at max level");
			swimIdleStaminaRegenMultiplierMin = Config.Bind("Swim Idling", "swimIdleStaminaRegenMultiplier", 0.3f, "Stamina regen multiplier when idle in the water and Swimming is at min level");
			swimIdleStaminaRegenMultiplierMax = Config.Bind("Swim Idling", "swimIdleStaminaRegenMultiplier", 0.9f, "Stamina regen multiplier when idle in the water and Swimming is at max level");


			forward_speed = ZSyncAnimation.GetHash("forward_speed");
			sideway_speed = ZSyncAnimation.GetHash("sideway_speed");
			turn_speed = ZSyncAnimation.GetHash("turn_speed");
			inWater = ZSyncAnimation.GetHash("inWater");
			onGround = ZSyncAnimation.GetHash("onGround");
			encumbered = ZSyncAnimation.GetHash("encumbered");
			flying = ZSyncAnimation.GetHash("flying");

			harmony.PatchAll();
        }

		void Update()
		{
			swimFasterKeyHeld = Input.GetKey(swimFasterKey.Value);
		}

		[HarmonyPatch(typeof(Player), nameof(Player.OnSwiming))]
		class FixOnSwiming
		{
			public static bool Prefix(Vector3 targetVel, float dt, Player __instance)
			{
				Player player = __instance;
				bool isLocalPlayer = player == Player.m_localPlayer;
				if (isLocalPlayer)
				{
					player.m_swimStaminaDrainMinSkill = swimStaminaDrainMinSkill.Value;
					player.m_swimStaminaDrainMaxSkill = swimStaminaDrainMaxSkill.Value;
				}

				float skillFactor = player.m_skills.GetSkillFactor(Skills.SkillType.Swim);
				//base.OnSwiming(targetVel, dt);
				if (targetVel.magnitude > 0.1f)
				{
					// Fast Swim Stamina drain Start
					float num = Mathf.Lerp(player.m_swimStaminaDrainMinSkill, player.m_swimStaminaDrainMaxSkill, skillFactor);

					if (isLocalPlayer && swimFasterKeyHeld)
					{
						num *= swimFasterStaminaMultiplier.Value;
					}

					player.UseStamina(dt * num);

					// Fast Swim Stamina drain End

					player.m_swimSkillImproveTimer += dt;

					if (player.m_swimSkillImproveTimer > 1f)
					{
						player.m_swimSkillImproveTimer = 0f;
						player.RaiseSkill(Skills.SkillType.Swim);
					}
				}
				else
				{
					// Swim Idle Stamina Regen Begin
					float maxStamina = player.GetMaxStamina();
					float num2 = (player.m_staminaRegen + (1f - player.m_stamina / maxStamina) * player.m_staminaRegen * player.m_staminaRegenTimeMultiplier);
					float staminaMultiplier = 1f;

					player.m_seman.ModifyStaminaRegen(ref staminaMultiplier);
					num2 *= staminaMultiplier;
					player.m_staminaRegenTimer -= dt;

					float swimIdleMultiplier = Mathf.Lerp(swimIdleStaminaRegenMultiplierMin.Value, swimIdleStaminaRegenMultiplierMax.Value, skillFactor);
					num2 *= swimIdleMultiplier;

					if (player.m_stamina < maxStamina && player.m_staminaRegenTimer <= 0f)
					{
						player.m_stamina = Mathf.Min(maxStamina, player.m_stamina + num2 * dt);
					}

					player.m_nview.GetZDO().Set("stamina", player.m_stamina);

					// Swim Idle Stamina Regen End
				}

				if (!player.HaveStamina())
				{
					player.m_drownDamageTimer += dt;
					if (player.m_drownDamageTimer > 1f)
					{
						player.m_drownDamageTimer = 0f;
						float damage = Mathf.Ceil(player.GetMaxHealth() / 20f);

						HitData hitData = new HitData();
						hitData.m_damage.m_damage = damage;
						hitData.m_point = player.GetCenterPoint();
						hitData.m_dir = Vector3.down;
						hitData.m_pushForce = 10f;
						player.Damage(hitData);

						Vector3 position = player.transform.position;
						position.y = player.m_waterLevel;
						player.m_drownEffects.Create(position, player.transform.rotation);
					}
				}
				
				return false;
			}
		}


		[HarmonyPatch(typeof(Character), nameof(Character.UpdateSwiming))]
		class FixSwimStaminaRegen
		{
			public static bool Prefix(float dt, ref Character __instance)
			{
				Character character = __instance;
				bool flag = character.IsOnGround();

				if (Mathf.Max(0f, character.m_maxAirAltitude - character.transform.position.y) > 0.5f && character.m_onLand != null)
				{
					character.m_onLand(new Vector3(character.transform.position.x, character.m_waterLevel, character.transform.position.z));
				}

				character.m_maxAirAltitude = character.transform.position.y;
				float speed = character.m_swimSpeed * character.GetAttackSpeedFactorMovement();

				// Scaled swim speed + Fast Swim Begin
				if (character == Player.m_localPlayer)
				{
					Player local = character as Player;
					float skillFactor = local.m_skills.GetSkillFactor(Skills.SkillType.Swim);
					float swimSpeedMultiplier = Mathf.Lerp(swimSpeedMultiplierMin.Value, swimSpeedMultiplierMax.Value, skillFactor);
					speed *= swimSpeedMultiplier;
					if (swimFasterKeyHeld)
						speed *= swimFasterSpeedMultiplier.Value;
				}

				// Scaled swim speed + Fast Swim End

				if (character.InMinorAction())
				{
					speed = 0f;
				}

				character.m_seman.ApplyStatusEffectSpeedMods(ref speed);
				Vector3 vector = character.m_moveDir * speed;
				if (vector.magnitude > 0f && character.IsOnGround())
				{
					vector = Vector3.ProjectOnPlane(vector, character.m_lastGroundNormal).normalized * vector.magnitude;
				}
				if (character.IsPlayer())
				{
					character.m_currentVel = Vector3.Lerp(character.m_currentVel, vector, character.m_swimAcceleration);
				}
				else
				{
					float magnitude = vector.magnitude;
					float magnitude2 = character.m_currentVel.magnitude;
					if (magnitude > magnitude2)
					{
						magnitude = Mathf.MoveTowards(magnitude2, magnitude, character.m_swimAcceleration);
						vector = vector.normalized * magnitude;
					}
					character.m_currentVel = Vector3.Lerp(character.m_currentVel, vector, 0.5f);
				}
				if (vector.magnitude > 0.1f)
				{
					character.AddNoise(15f);
				}
				character.AddPushbackForce(ref character.m_currentVel);
				Vector3 vector2 = character.m_currentVel - character.m_body.velocity;
				vector2.y = 0f;

				if (vector2.magnitude > 20f)
				{
					vector2 = vector2.normalized * 20f;
				}
				character.m_body.AddForce(vector2, (ForceMode)2);
				float num = character.m_waterLevel - character.m_swimDepth;

				if (character.transform.position.y < num)
				{
					float t = Mathf.Clamp01((num - character.transform.position.y) / 2f);
					float target = Mathf.Lerp(0f, 10f, t);
					Vector3 velocity = character.m_body.velocity;
					velocity.y = Mathf.MoveTowards(velocity.y, target, 50f * dt);
					character.m_body.velocity = velocity;
				}
				else
				{
					float t2 = Mathf.Clamp01((0f - (num - character.transform.position.y)) / 1f);
					float num2 = Mathf.Lerp(0f, 10f, t2);
					Vector3 velocity2 = character.m_body.velocity;
					velocity2.y = Mathf.MoveTowards(velocity2.y, 0f - num2, 30f * dt);
					character.m_body.velocity = velocity2;
				}

				float target2 = 0f;
				if (character.m_moveDir.magnitude > 0.1f || character.AlwaysRotateCamera())
				{
					float speed2 = character.m_swimTurnSpeed;
					character.m_seman.ApplyStatusEffectSpeedMods(ref speed2);
					target2 = character.UpdateRotation(speed2, dt);
				}

				character.m_body.angularVelocity = Vector3.zero;
				character.UpdateEyeRotation();
				character.m_body.useGravity = true;

				float num3 = Vector3.Dot(character.m_currentVel, character.transform.forward);
				float value = Vector3.Dot(character.m_currentVel, character.transform.right);
				float num4 = Vector3.Dot(character.m_body.velocity, character.transform.forward);

				character.m_currentTurnVel = Mathf.SmoothDamp(character.m_currentTurnVel, target2, ref character.m_currentTurnVelChange, 0.5f, 99f);
				character.m_zanim.SetFloat(forward_speed, character.IsPlayer() ? num3 : num4);
				character.m_zanim.SetFloat(sideway_speed, value);
				character.m_zanim.SetFloat(turn_speed, character.m_currentTurnVel);
				character.m_zanim.SetBool(inWater, !flag);
				character.m_zanim.SetBool(onGround, value: false);
				character.m_zanim.SetBool(encumbered, value: false);
				character.m_zanim.SetBool(flying, value: false);
				if (!flag)
				{
					if (character is Player)
					{
						Player p = character as Player;
						p.OnSwiming(vector, dt);
					}
					else
					{
						character.OnSwiming(vector, dt);
					}

				}
				return false;
			}
		}
    }
}
