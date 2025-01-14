﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhenoPad.PhenotypeService
{
    public class Phenotype : IEquatable<Phenotype>, INotifyPropertyChanged
    {
        public string hpId { get; set; }
        public string name { get; set; }
        public List<string> alternatives { get; set; }
        public int state { get; set; } // NA: -1, Y: 1, N: 0
        public int pageSource { get; set; }
        public SourceType sourceType { get; set; }

        public DateTime time;


        public Phenotype()
        {
        }

        // Initiate from phenopad
        public Phenotype(string hpid, string name, int state, int page,List<String> alter = null, SourceType st = SourceType.None)
        {
            this.hpId = hpid;
            this.name = name;
            this.alternatives = (alter==null)? new List<string>():alter;
            this.state = state;
            pageSource = page;
            sourceType = st;
        }

        // Initiate from json of Phenotips
        public Phenotype(Row row)
        {
            this.hpId = row.id;
            this.name = row.name;
            this.alternatives = row.synonym;
            this.state = -1;
        }

        // Initiate from NCR
        public Phenotype(NCRPhenotype p)
        {
            this.hpId = p.hp_id;
            this.name = p.names[0];
            this.state = -1;
        }
        public Phenotype(SuggestPhenotype sp)
        {
            this.hpId = sp.id;
            this.name = sp.name;
            this.alternatives = new List<string>();
            this.state = -1;
        }
        

        [System.Xml.Serialization.XmlIgnore]
        public Action<Phenotype> OnRemoveCallback { get; set; }
        public void OnRemove()
        {
            OnRemoveCallback(this);
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged(string name)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(name));
            }
        }

        public Phenotype Clone()
        {
            Phenotype p = new Phenotype();
            p.hpId = this.hpId;
            p.name = this.name;
            p.state = this.state;
            p.pageSource = this.pageSource;
            p.alternatives = this.alternatives;
            p.sourceType = this.sourceType;
            return p;
        }

        public override bool Equals(object obj)
        {
            var phenotype = obj as Phenotype;
            return phenotype != null &&
                   hpId.Equals(phenotype.hpId);
        }

        public bool Equals(Phenotype other)
        {
            return other != null &&
                   hpId.Equals(other.hpId);
        }

        public override int GetHashCode()
        {
            return -1032463776 + EqualityComparer<string>.Default.GetHashCode(hpId);
        }

        public static bool operator ==(Phenotype phenotype1, Phenotype phenotype2)
        {
            return EqualityComparer<Phenotype>.Default.Equals(phenotype1, phenotype2);
        }

        public static bool operator !=(Phenotype phenotype1, Phenotype phenotype2)
        {
            return !(phenotype1 == phenotype2);
        }
    }
}
