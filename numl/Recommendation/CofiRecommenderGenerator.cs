﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using numl.Math.LinearAlgebra;
using numl.Supervised;
using numl.Math;
using numl.Model;
using numl.Optimization.Functions;

namespace numl.Recommendation
{
    /// <summary>
    /// Collaborative Filtering Recommender generator.
    /// </summary>
    public class CofiRecommenderGenerator : Generator
    {
        /// <summary>
        /// Gets or sets the Range of the ratings, values outside of this will be treated as not provided.
        /// </summary>
        public Range Ratings { get; set; }

        /// <summary>
        /// Gets or sets the number of Collaborative Features to learn.
        /// <para>Each learned feature is independently obtained of other learned features.</para>
        /// </summary>
        public int CollaborativeFeatures { get; set; }

        /// <summary>
        /// Gets or sets the learning rate (alpha).
        /// </summary>
        public double LearningRate { get; set; }

        /// <summary>
        /// Gets or sets the regularisation term Lambda.
        /// </summary>
        public double Lambda { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of training iterations to perform when optimizing.
        /// </summary>
        public int MaxIterations { get; set; }

        /// <summary>
        /// Initialises a new Collaborative Filtering generator.
        /// </summary>
        public CofiRecommenderGenerator()
        {
            this.NormalizeFeatures = true;

            this.MaxIterations = 100;
            this.LearningRate = 0.1;

            this.FeatureNormalizer = new Preprocessing.Normalization.ZeroMeanFeatureNormalizer();
        }

        /// <summary>
        /// Generates a new Collaborative Filtering model.
        /// </summary>
        /// <param name="X">Training matrix values.</param>
        /// <param name="y">Vector of entity identifiers.</param>
        /// <returns></returns>
        public override IModel Generate(Matrix X, Vector y)
        {
            this.Preprocess(X.Copy(), y.Copy());

            // inputs are ratings from each user (X = entities x ratings), y = entity id.
            // create rating range in case we don't have one already
            if (this.Ratings == null)
                this.Ratings = new Range() { Min = y.Where(w => w > 0d).Min(), Max = y.Max() };

            // indicator matrix of 1's where rating was provided otherwise 0's.
            Matrix R = X.ToBinary(f => this.Ratings.Test(f));

            // The mean needs to be values within rating range only.
            Vector mean = (from i in X.GetRows()
                           select i.Where(w => this.Ratings.Test(w)).Sum() / 
                           i.Where(w => this.Ratings.Test(w)).Count()).ToVector();

            // update feature averages before preprocessing features.
            this.FeatureProperties.Average = mean;

            this.Preprocess(X, y);

            // where references could be user ratings and entities are movies / books, etc.
            int references = X.Cols, entities = X.Rows;

            // initialize Theta parameters
            Matrix ThetaX = Matrix.Rand(entities, this.CollaborativeFeatures, -1d);
            Matrix ThetaY = Matrix.Rand(references, this.CollaborativeFeatures, -1d);

            ICostFunction costFunction = new Optimization.Functions.CostFunctions.CofiCostFunction()
            {
                CollaborativeFeatures = this.CollaborativeFeatures,
                Lambda = this.Lambda,
                R = R,
                Regularizer = null,
                X = ThetaX,
                Y = X.Unshape()
            };

            // we're optimising two params so combine them
            Vector Theta = Vector.Combine(ThetaX.Unshape(), ThetaY.Unshape());

            Optimization.Optimizer optimizer = new Optimization.Optimizer(Theta, this.MaxIterations, this.LearningRate)
            {
                CostFunction = costFunction
            };

            optimizer.Run();

            // extract the optimised parameter Theta
            ThetaX = optimizer.Properties.Theta.Slice(0, (ThetaX.Rows * ThetaX.Cols) - 1).Reshape(entities, VectorType.Row);
            ThetaY = optimizer.Properties.Theta.Slice(ThetaX.Rows * ThetaX.Cols, Theta.Length - 1).Reshape(references, VectorType.Row);

            // create reference mappings, each value is the original index.
            Vector referenceMap = Vector.Create(references, i => i);
            Vector entityMap = Vector.Create(entities, i => i);

            return new CofiRecommenderModel(referenceMap, entityMap)
            {
                Descriptor = this.Descriptor,
                NormalizeFeatures = this.NormalizeFeatures,
                FeatureNormalizer = this.FeatureNormalizer,
                FeatureProperties = this.FeatureProperties,
                Mu = mean,
                Y = y,
                Reference = X,
                ThetaX = ThetaX,
                ThetaY = ThetaY
            };
        }
    }
}