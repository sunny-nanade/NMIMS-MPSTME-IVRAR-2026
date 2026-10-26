using System.Collections;
using UnityEngine;

namespace CSER
{
    /// Wires the physical puzzles: evidence box, shredder, quarantine container, waste bin and the briefcase.
    public class RoomPuzzles : MonoBehaviour
    {
        public DropZone evidenceBox, shredder, quarantine, wasteBin;
        public Transform shredderBody;
        public Hinge briefcaseLid;
        public int confidentialCount = 2;

        int evidence, shredded;

        void Start()
        {
            evidenceBox.Accepted += item =>
            {
                item.Deposit(evidenceBox.GetComponent<Collider>().bounds);
                Sfx.PlayAt(Sfx.I ? Sfx.I.drop : null, item.transform.position);
                GameManager.I.Complete(item.kind == ItemKind.EvidencePrintout ? TaskId.EvidencePrintout : TaskId.EvidenceNote);
            };
            shredder.Accepted += item =>
            {
                item.Consume(shredder.intake, 0.35f, 0.3f);
                StartCoroutine(Shred());
                if (++shredded >= confidentialCount) GameManager.I.Complete(TaskId.ShredDocs);
                else GameUI.I?.Caption($"Shredded. {confidentialCount - shredded} confidential document left.", Palette.Ink);
            };
            quarantine.Accepted += item =>
            {
                item.Consume(quarantine.intake, 0.4f, 0.05f);
                Sfx.PlayAt(Sfx.I ? Sfx.I.drop : null, item.transform.position);
                GameManager.I.Complete(TaskId.QuarantineUSB);
            };
            wasteBin.Accepted += item =>
            {
                item.Consume(wasteBin.intake, 0.4f, 0.1f);
                GameUI.I?.Caption("Just a lunch menu, fine for the bin.", Palette.Ink);
            };
        }

        IEnumerator Shred()
        {
            Sfx.PlayAt(Sfx.I ? Sfx.I.shred : null, shredder.transform.position, 0.9f);
            if (!shredderBody) yield break;
            Vector3 p = shredderBody.localPosition;
            for (float t = 0; t < 1.2f; t += Time.deltaTime)
            {
                shredderBody.localPosition = p + Random.insideUnitSphere * 0.003f;
                yield return null;
            }
            shredderBody.localPosition = p;
        }

        public void OpenBriefcase()
        {
            if (briefcaseLid && !briefcaseLid.IsOpen)
            {
                briefcaseLid.Open();
                GameUI.I?.Caption("Fred's briefcase: a hardware token... and a USB stick labelled 'SALARIES 2026'. That isn't his.", Palette.Ink, 6f);
            }
        }
    }
}
